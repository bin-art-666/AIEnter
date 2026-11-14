// ==WindhawkMod==
// @id              aienter-taskbar-ai
// @name            AIEnter AI beside clock
// @description     Adds an AI button in the real taskbar layout immediately after the clock.
// @version         0.1.0
// @author          AIEnter
// @include         explorer.exe
// @architecture    x86-64
// @compilerOptions -lole32 -loleaut32 -lruntimeobject -lshell32 -luuid
// ==/WindhawkMod==
// SPDX-License-Identifier: MIT
// Taskbar discovery, dispatch, XAML access and slot leasing adapted from
// Taskbar Folder Menus 2.1, copyright (c) 2026 sb4ssman and contributors.
// https://github.com/ramensoftware/windhawk-mods/blob/main/mods/taskbar-folder-menus.wh.cpp
// See ThirdParty/TaskbarIntegration-MIT.txt in the AIEnter source distribution.

// ==WindhawkModSettings==
/*
- executable: ""
  $name: AIEnter executable path
*/
// ==/WindhawkModSettings==
#include <windows.h>
#include <shellapi.h>
#include <windhawk_utils.h>
#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Input.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>
#include <algorithm>
#include <atomic>
#include <string>
#include <thread>
#include <vector>
namespace folder_menus_slot_lease {

using winrt::Windows::UI::Xaml::FrameworkElement;
using winrt::Windows::UI::Xaml::GridUnitType;
using winrt::Windows::UI::Xaml::Controls::ColumnDefinition;
using winrt::Windows::UI::Xaml::Controls::Grid;
using winrt::Windows::UI::Xaml::Controls::Panel;
using winrt::Windows::UI::Xaml::Controls::StackPanel;

enum class Anchor {
    BeforeIcons,
    BeforeOmni,
    BeforeClock,
    AfterClock,
    AfterShowDesktop,
};

// Which layout contract the live tray panel follows.
enum class Kind {
    Unsupported,  // some other Panel: do not guess its layout semantics.
    Columns,      // Grid. A slot is a column index.
    Order,        // StackPanel. A slot is a child index.
};

struct Lease {
    std::wstring markerName;
    int slot = -1;
    Kind kind = Kind::Unsupported;
};

inline Kind Classify(FrameworkElement const& parent) {
    if (!parent)
        return Kind::Unsupported;
    if (parent.try_as<Grid>())
        return Kind::Columns;
    if (parent.try_as<StackPanel>())
        return Kind::Order;
    return Kind::Unsupported;
}

// Class name of an unexpected panel, so a mod can log what it actually got
// instead of reporting a bare "not found" for an element that is right there.
inline std::wstring ClassName(FrameworkElement const& element) {
    if (!element)
        return L"(null)";
    try {
        return std::wstring(winrt::get_class_name(element));
    } catch (...) {
        return L"(unknown)";
    }
}

// Named direct children, for logging when an anchor cannot be resolved. A tray
// restructure shows up here as missing or renamed names, which is the one thing
// a user's debug log otherwise cannot tell us.
inline std::wstring DescribeChildren(Panel const& parent) {
    if (!parent)
        return L"(none)";
    std::wstring names;
    try {
        for (auto const& child : parent.Children()) {
            auto element = child.try_as<FrameworkElement>();
            if (!element || element.Name().empty())
                continue;
            if (!names.empty())
                names += L", ";
            names += element.Name();
        }
    } catch (...) {
        return L"(unreadable)";
    }
    return names.empty() ? L"(no named children)" : names;
}

inline FrameworkElement FindDirectChild(Panel const& parent,
                                        wchar_t const* name) {
    if (!parent || !name)
        return nullptr;
    for (auto const& child : parent.Children()) {
        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == name)
            return element;
    }
    return nullptr;
}

inline int IndexOfChild(Panel const& parent, FrameworkElement const& child) {
    if (!parent || !child)
        return -1;
    for (uint32_t i = 0; i < parent.Children().Size(); ++i) {
        if (parent.Children().GetAt(i).try_as<FrameworkElement>() == child)
            return static_cast<int>(i);
    }
    return -1;
}

inline bool ResolveSlot(Panel const& parent, Anchor anchor, int& slot) {
    Kind kind = Classify(parent);
    if (kind == Kind::Unsupported)
        return false;

    if (anchor == Anchor::BeforeIcons) {
        slot = 0;
        return true;
    }

    wchar_t const* referenceName = nullptr;
    bool after = false;
    switch (anchor) {
        case Anchor::BeforeOmni:
            referenceName = L"ControlCenterButton";
            break;
        case Anchor::BeforeClock:
            referenceName = L"NotificationCenterButton";
            break;
        case Anchor::AfterClock:
            referenceName = L"NotificationCenterButton";
            after = true;
            break;
        case Anchor::AfterShowDesktop:
            referenceName = L"ShowDesktopStack";
            after = true;
            break;
        case Anchor::BeforeIcons:
            break;
    }

    auto reference = FindDirectChild(parent, referenceName);
    if (!reference)
        return false; // Never silently turn an unavailable anchor into slot 0.

    if (kind == Kind::Columns) {
        slot = Grid::GetColumn(reference) +
               (after ? std::max(1, Grid::GetColumnSpan(reference)) : 0);
        return true;
    }

    // Order: the reference's own child index is the slot. There is no span to
    // step over -- a StackPanel child occupies exactly one position.
    int index = IndexOfChild(parent, reference);
    if (index < 0)
        return false;
    slot = index + (after ? 1 : 0);
    return true;
}

inline bool Release(Panel const& parent, Lease& lease);

inline bool AcquireAt(Panel const& parent, int slot,
                      std::wstring const& markerName, Lease& lease) {
    Kind kind = Classify(parent);
    if (kind == Kind::Unsupported || slot < 0 || markerName.empty())
        return false;

    // A marker with this name already in the tray is either this caller's own
    // live lease — refuse, acquiring twice would strand the first slot — or a
    // leftover from an instance whose teardown never reached the UI thread.
    // Refusing the leftover would block every later apply until Explorer
    // restarts, so release it and take the slot fresh, correcting the
    // requested slot if the released one sat before it.
    if (auto stale = FindDirectChild(parent, markerName.c_str())) {
        if (lease.markerName == markerName)
            return false;
        int staleSlot = kind == Kind::Columns
                            ? Grid::GetColumn(stale)
                            : IndexOfChild(parent, stale);
        Lease leftover{markerName, staleSlot, kind};
        if (!Release(parent, leftover))
            return false;
        if (staleSlot >= 0 && staleSlot < slot)
            --slot;
    }

    Grid marker;
    marker.Name(markerName);
    marker.Width(0.0);
    marker.Height(0.0);
    marker.IsHitTestVisible(false);

    if (kind == Kind::Columns) {
        auto grid = parent.try_as<Grid>();
        if (!grid)
            return false;
        ColumnDefinition definition;
        definition.Width({1.0, GridUnitType::Auto});
        if (static_cast<uint32_t>(slot) < grid.ColumnDefinitions().Size())
            grid.ColumnDefinitions().InsertAt(slot, definition);
        else
            grid.ColumnDefinitions().Append(definition);

        for (auto const& child : grid.Children()) {
            auto element = child.try_as<FrameworkElement>();
            if (!element) continue;
            int start = Grid::GetColumn(element);
            int span = Grid::GetColumnSpan(element);
            if (start >= slot)
                Grid::SetColumn(element, start + 1);
            else if (start + span > slot)
                Grid::SetColumnSpan(element, span + 1);
        }

        Grid::SetColumn(marker, slot);
        grid.Children().Append(marker);
    } else {
        // Order: no column is created or owned. The marker simply holds the
        // position, and PlaceChild drops the content next to it.
        uint32_t index = std::min(static_cast<uint32_t>(slot),
                                  parent.Children().Size());
        parent.Children().InsertAt(index, marker);
        slot = static_cast<int>(index);
    }

    lease = {markerName, slot, kind};
    return true;
}

// Live index of the lease marker. Other mods inject and remove siblings around
// us, so the acquire-time index is a hint, never the truth at removal time.
inline bool FindMarker(Panel const& parent, Lease const& lease,
                       uint32_t& index) {
    if (!parent || lease.markerName.empty())
        return false;
    for (uint32_t i = 0; i < parent.Children().Size(); ++i) {
        auto element = parent.Children().GetAt(i).try_as<FrameworkElement>();
        if (element && element.Name() == lease.markerName) {
            index = i;
            return true;
        }
    }
    return false;
}

// Put mod content into the leased slot. On a Grid the content joins the leased
// column; on a StackPanel it is inserted directly after the marker, so the
// marker's position is the content's position.
inline bool PlaceChild(Panel const& parent, Lease const& lease,
                       FrameworkElement const& content) {
    if (!parent || !content || lease.kind == Kind::Unsupported)
        return false;

    uint32_t markerIndex = 0;
    if (!FindMarker(parent, lease, markerIndex))
        return false;

    if (lease.kind == Kind::Columns) {
        auto marker = parent.Children().GetAt(markerIndex)
                          .try_as<FrameworkElement>();
        Grid::SetColumn(content, marker ? Grid::GetColumn(marker) : lease.slot);
        parent.Children().Append(content);
        return true;
    }

    parent.Children().InsertAt(markerIndex + 1, content);
    return true;
}

inline bool Release(Panel const& parent, Lease& lease) {
    if (!parent || lease.markerName.empty())
        return false;

    uint32_t markerIndex = 0;
    if (!FindMarker(parent, lease, markerIndex))
        return false;

    if (lease.kind == Kind::Order) {
        parent.Children().RemoveAt(markerIndex);
        lease = {};
        return true;
    }

    auto grid = parent.try_as<Grid>();
    auto marker =
        parent.Children().GetAt(markerIndex).try_as<FrameworkElement>();
    int liveColumn = marker ? Grid::GetColumn(marker) : lease.slot;
    if (!grid || liveColumn < 0)
        return false;

    grid.Children().RemoveAt(markerIndex);
    if (static_cast<uint32_t>(liveColumn) < grid.ColumnDefinitions().Size())
        grid.ColumnDefinitions().RemoveAt(liveColumn);

    for (auto const& child : grid.Children()) {
        auto element = child.try_as<FrameworkElement>();
        if (!element) continue;
        int start = Grid::GetColumn(element);
        int span = Grid::GetColumnSpan(element);
        if (start > liveColumn)
            Grid::SetColumn(element, start - 1);
        else if (start < liveColumn && start + span > liveColumn)
            Grid::SetColumnSpan(element, std::max(1, span - 1));
    }

    lease = {};
    return true;
}

}  // namespace folder_menus_slot_lease

namespace folder_menus_taskbar_window {

// ---- Window discovery -------------------------------------------------------

inline HWND FindCurrentProcessTaskbarWnd() {
    HWND result = nullptr;
    EnumWindows(
        [](HWND window, LPARAM parameter) -> BOOL {
            DWORD processId = 0;
            WCHAR className[32];
            if (GetWindowThreadProcessId(window, &processId) &&
                processId == GetCurrentProcessId() &&
                GetClassName(window, className, ARRAYSIZE(className)) &&
                _wcsicmp(className, L"Shell_TrayWnd") == 0) {
                *reinterpret_cast<HWND*>(parameter) = window;
                return FALSE;
            }
            return TRUE;
        },
        reinterpret_cast<LPARAM>(&result));
    return result;
}

// Shell_TrayWnd can be recreated inside Explorer. A cache is useful only while
// it names a live window; otherwise rediscover before dispatch or teardown.
inline HWND ResolveTaskbarWnd(HWND cached) {
    if (cached && IsWindow(cached))
        return cached;
    return FindCurrentProcessTaskbarWnd();
}

}  // namespace folder_menus_taskbar_window

namespace folder_menus_dispatch {

// ---- UI-thread marshalling --------------------------------------------------
//
// XAML may only be touched from the thread that owns it. This posts work onto
// the taskbar's thread with a CALLWNDPROC hook and a private registered
// message, and reports whether the callback actually ran — a caller that
// assumes it did will corrupt its own state when the dispatch failed.

using ThreadProc = void (*)(void*);
using ExceptionLogFn = void (*)(PCWSTR context);

inline ExceptionLogFn g_logException = nullptr;

// Point this at the mod's logger once in Wh_ModInit so failures inside a UI
// callback are reported in the mod's own voice.
inline void SetExceptionLogger(ExceptionLogFn logger) {
    g_logException = logger;
}

inline bool Invoke(ThreadProc proc, void* parameter) {
    try {
        proc(parameter);
        return true;
    } catch (...) {
        if (g_logException) g_logException(L"UI callback");
    }
    return false;
}

struct Dispatch {
    ThreadProc proc;
    void* parameter;
    bool succeeded = false;
    // Every concurrent caller installs its own hook with this same proc, and
    // each hook instance sees every message equal to g_dispatchMessage. With
    // two dispatches in flight, both hooks are in the chain when either
    // message arrives, so without this each callback would run twice. The
    // hooks run one after another on the UI thread, so a plain flag suffices.
    bool ran = false;
};

// The private message this mod dispatches on. Set before the hook is
// installed, and read by the hook proc to recognise its own message.
//
// A CALLWNDPROC HOOK SEES EVERY MESSAGE SENT TO EVERY WINDOW ON THE TASKBAR'S
// UI THREAD. `lParam` for all of those is arbitrary — an integer, a flag, a
// pointer to something else entirely. So the message MUST be checked first,
// against a value that does not come from lParam, and only then may lParam be
// treated as a Dispatch*. Reading anything out of lParam before that check
// dereferences whatever happened to be in the message and takes Explorer down
// with it.
//
// Atomic because the caller may be the retry thread while the hook
// proc runs on the taskbar's UI thread. RegisterWindowMessageW returns the
// same value for the same string for the lifetime of the session, so this
// settles on one value immediately and never changes again.
inline std::atomic<UINT> g_dispatchMessage{0};

// messageName must embed WH_MOD_ID, so two mods cannot collide on the message.
inline bool RunFromWindowThread(HWND window, ThreadProc proc, void* parameter,
                                PCWSTR messageName) {
    UINT message = RegisterWindowMessageW(messageName);
    if (!message) return false;

    DWORD threadId = GetWindowThreadProcessId(window, nullptr);
    if (!threadId) return false;
    if (threadId == GetCurrentThreadId()) return Invoke(proc, parameter);

    g_dispatchMessage.store(message, std::memory_order_release);

    HHOOK hook = SetWindowsHookExW(
        WH_CALLWNDPROC,
        [](int code, WPARAM wParam, LPARAM lParam) -> LRESULT {
            if (code == HC_ACTION) {
                auto const* call = reinterpret_cast<CWPSTRUCT const*>(lParam);
                // Message first. Only our own private message carries a
                // Dispatch* in lParam; everything else carries something we
                // must not touch.
                UINT expected =
                    g_dispatchMessage.load(std::memory_order_acquire);
                if (expected && call->message == expected) {
                    if (auto* dispatch =
                            reinterpret_cast<Dispatch*>(call->lParam);
                        dispatch && !dispatch->ran) {
                        dispatch->ran = true;
                        dispatch->succeeded =
                            Invoke(dispatch->proc, dispatch->parameter);
                    }
                }
            }
            return CallNextHookEx(nullptr, code, wParam, lParam);
        },
        nullptr, threadId);
    if (!hook) return false;

    Dispatch dispatch{proc, parameter};
    SendMessageW(window, message, 0, reinterpret_cast<LPARAM>(&dispatch));
    UnhookWindowsHookEx(hook);
    return dispatch.succeeded;
}

}  // namespace folder_menus_dispatch

namespace dispatch = folder_menus_dispatch;
namespace folder_menus_taskbar_xaml {

using winrt::Windows::UI::Xaml::FrameworkElement;
using winrt::Windows::UI::Xaml::XamlRoot;

// ---- XamlRoot ---------------------------------------------------------------

using CTaskBand_GetTaskbarHost_t = void*(WINAPI*)(void*, void*);
using TaskbarHost_FrameHeight_t = int(WINAPI*)(void*);
using Ref_count_base_Decref_t = void(WINAPI*)(void*);
using TrayUI_StartTaskbar_t = void(WINAPI*)(void*);

inline CTaskBand_GetTaskbarHost_t CTaskBand_GetTaskbarHost_Original = nullptr;
inline TaskbarHost_FrameHeight_t TaskbarHost_FrameHeight_Original = nullptr;
inline Ref_count_base_Decref_t Ref_count_base_Decref_Original = nullptr;
inline TrayUI_StartTaskbar_t TrayUI_StartTaskbar_Original = nullptr;
inline void* CTaskBand_ITaskListWndSite_vftable = nullptr;

// The mod's rebuild callback, invoked after Explorer rebuilds the taskbar.
inline void (*g_onTaskbarRebuilt)() = nullptr;

inline void WINAPI TrayUI_StartTaskbar_Hook(void* self) {
    TrayUI_StartTaskbar_Original(self);
    try {
        if (g_onTaskbarRebuilt) g_onTaskbarRebuilt();
    } catch (...) {
        if (dispatch::g_logException)
            dispatch::g_logException(L"TrayUI::StartTaskbar hook");
    }
}

inline bool HookTaskbarSymbols(void (*onTaskbarRebuilt)()) {
    g_onTaskbarRebuilt = onTaskbarRebuilt;
    HMODULE taskbar = LoadLibraryExW(L"taskbar.dll", nullptr,
                                     LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!taskbar) return false;
    WindhawkUtils::SYMBOL_HOOK taskbarDllHooks[] = {
        {{LR"(const CTaskBand::`vftable'{for `ITaskListWndSite'})"},
         &CTaskBand_ITaskListWndSite_vftable},
        {{LR"(public: virtual class std::shared_ptr<class TaskbarHost> __cdecl CTaskBand::GetTaskbarHost(void)const )"},
         &CTaskBand_GetTaskbarHost_Original},
        {{LR"(public: int __cdecl TaskbarHost::FrameHeight(void)const )"},
         &TaskbarHost_FrameHeight_Original},
        {{LR"(public: void __cdecl std::_Ref_count_base::_Decref(void))"},
         &Ref_count_base_Decref_Original},
        {{LR"(public: virtual void __cdecl TrayUI::StartTaskbar(void))"},
         &TrayUI_StartTaskbar_Original, TrayUI_StartTaskbar_Hook},
    };
    return WindhawkUtils::HookSymbols(taskbar, taskbarDllHooks,
                                      ARRAYSIZE(taskbarDllHooks));
}

// The FrameworkElement lives at an offset inside TaskbarHost that MOVES
// between Windows builds, so it is read out of TaskbarHost::FrameHeight's
// prologue at runtime rather than hardcoded.
inline size_t FrameworkElementOffset() {
    size_t offset = 0x10;
#if defined(_M_X64)
    BYTE const* code =
        reinterpret_cast<BYTE const*>(TaskbarHost_FrameHeight_Original);
    if (code[0] == 0x48 && code[1] == 0x83 && code[2] == 0xEC &&
        code[4] == 0x48 && code[5] == 0x83 && code[6] == 0xC1 &&
        code[7] <= 0x7F) {
        offset = code[7];
    }
#elif defined(_M_ARM64)
    DWORD const* code =
        reinterpret_cast<DWORD const*>(TaskbarHost_FrameHeight_Original);
    if (code[0] == 0xD503237F && (code[1] & 0xFFC07FFF) == 0xA9807BFD &&
        code[2] == 0x910003FD && (code[3] & 0xFFF00FE0) == 0xF8400C00) {
        offset = (code[3] >> 12) & 0xFF;
    }
#else
#error "Unsupported architecture"
#endif
    return offset;
}

inline XamlRoot GetTaskbarXamlRoot(HWND taskbarWnd) {
    if (!CTaskBand_GetTaskbarHost_Original ||
        !TaskbarHost_FrameHeight_Original || !Ref_count_base_Decref_Original ||
        !CTaskBand_ITaskListWndSite_vftable)
        return nullptr;

    HWND taskSwWnd = (HWND)GetProp(taskbarWnd, L"TaskbandHWND");
    if (!taskSwWnd) return nullptr;
    void* taskBand = (void*)GetWindowLongPtr(taskSwWnd, 0);
    if (!taskBand) return nullptr;

    void* site = taskBand;
    for (int i = 0; *(void**)site != CTaskBand_ITaskListWndSite_vftable; ++i) {
        if (i == 20) return nullptr;
        site = (void**)site + 1;
    }

    void* host[2]{};
    CTaskBand_GetTaskbarHost_Original(site, host);
    if (!host[0] || !host[1]) {
        if (host[1]) Ref_count_base_Decref_Original(host[1]);
        return nullptr;
    }

    auto* unknown =
        *(IUnknown**)((BYTE*)host[0] + FrameworkElementOffset());
    if (!unknown) {
        Ref_count_base_Decref_Original(host[1]);
        return nullptr;
    }
    FrameworkElement element = nullptr;
    unknown->QueryInterface(winrt::guid_of<FrameworkElement>(),
                            winrt::put_abi(element));
    auto result = element ? element.XamlRoot() : nullptr;
    Ref_count_base_Decref_Original(host[1]);
    return result;
}

}  // namespace folder_menus_taskbar_xaml

namespace xaml = winrt::Windows::UI::Xaml;
namespace controls = winrt::Windows::UI::Xaml::Controls;
namespace slot = folder_menus_slot_lease;
namespace taskbar = folder_menus_taskbar_xaml;

static HANDLE g_stop = nullptr, g_refresh = nullptr, g_appearanceEvent = nullptr, g_mapping = nullptr;
static volatile LONG* g_shared = nullptr;
struct AppearanceState { LONG magic = 0x41494531, pid = 0, icon = 26, enabled = 1, range = 9, intensity = 77, open = 0, sequence = 0; };
static AppearanceState g_appearance;
static AppearanceState ReadAppearance() {
    AppearanceState state;
    for (int attempt = 0; attempt < 32 && g_shared; ++attempt) {
        LONG before = g_shared[7]; if (before & 1) { Sleep(1); continue; }
        MemoryBarrier();
        LONG values[8]; for (int i = 0; i < 8; ++i) values[i] = g_shared[i];
        MemoryBarrier();
        if (before != g_shared[7]) { Sleep(1); continue; }
        if (values[0] != 0x41494531) break;
        state = {values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]};
        break;
    }
    state.icon = std::clamp(state.icon, 16L, 36L); state.range = std::clamp(state.range, 0L, 12L);
    state.intensity = std::clamp(state.intensity, 0L, 100L);
    return state;
}
static std::thread g_worker;
static std::wstring g_executable;
static std::atomic<bool> g_unloading{false};
// XAML references must be released on the taskbar thread, never at DLL teardown.
struct AttachedButton {
    controls::Panel parent{nullptr};
    controls::Button button{nullptr};
    controls::Grid glyphHost{nullptr}, glow{nullptr};
    xaml::Shapes::Ellipse icon{nullptr};
    xaml::Media::Imaging::BitmapImage artwork{nullptr};
    winrt::event_token artworkOpened{}, artworkFailed{};
    int lastX = -1, lastRight = -1, lastWidth = -1;
    slot::Lease lease;
    winrt::event_token click{}, rightTap{}, layout{};
};
static AttachedButton* g_attached = nullptr;
static void LogFailure(PCWSTR context);
static void UpdateButtonAppearance() {
    if (!g_attached) return;
    auto& state = *g_attached;
    double availableHeight = std::max(16.0, state.parent.ActualHeight() - 4);
    double iconSize = std::min((double)g_appearance.icon, availableHeight);
    double range = g_appearance.enabled ? (double)g_appearance.range : 0;
    double extent = iconSize + 2 * range;
    state.button.Width(extent + 4);
    state.glyphHost.Width(extent); state.glyphHost.Height(std::min(extent, availableHeight));
    xaml::Media::RectangleGeometry clip;
    clip.Rect({0, 0, (float)extent, (float)std::min(extent, availableHeight)});
    state.glyphHost.Clip(clip);
    state.icon.Width(iconSize); state.icon.Height(iconSize);
    state.glow.Children().Clear();
    for (int layer = 0; layer < 16; ++layer) {
        double fraction = (16 - layer) / 16.0;
        winrt::Windows::UI::Xaml::Shapes::Ellipse halo;
        halo.Width(iconSize + 2 * range * fraction); halo.Height(iconSize + 2 * range * fraction);
        halo.HorizontalAlignment(xaml::HorizontalAlignment::Center); halo.VerticalAlignment(xaml::VerticalAlignment::Center);
        halo.Fill(xaml::Media::SolidColorBrush(winrt::Windows::UI::Color{(BYTE)(5 + layer / 3), 115, 145, 255}));
        state.glow.Children().Append(halo);
    }
    bool active = g_appearance.open && g_appearance.enabled && range > 0 && g_appearance.intensity > 0;
    state.glow.Opacity(active ? g_appearance.intensity / 100.0 : 0.0);
    Wh_SetIntValue(L"GlowActive", active ? 1 : 0);
    Wh_SetIntValue(L"GlowRange", g_appearance.range); Wh_SetIntValue(L"GlowIntensity", g_appearance.intensity);
    Wh_SetIntValue(L"ArtworkCircleCropped", 1); Wh_SetIntValue(L"ArtworkSize", (int)iconSize); Wh_SetIntValue(L"ButtonWidth", (int)state.button.Width());
}
static void ApplyAppearanceState(void* parameter) {
    if (g_unloading) return;
    g_appearance = *static_cast<AppearanceState*>(parameter);
    UpdateButtonAppearance();
}


static void LaunchAi(bool settings = false) {
    if (g_unloading) return;
    if (GetFileAttributesW(g_executable.c_str()) == INVALID_FILE_ATTRIBUTES) {
        Wh_Log(L"AIEnter executable is missing: %s", g_executable.c_str());
        return;
    }
    AllowSetForegroundWindow(ASFW_ANY);
    SHELLEXECUTEINFOW info{sizeof(info)};
    info.fMask = SEE_MASK_FLAG_NO_UI;
    info.lpFile = g_executable.c_str();
    info.lpParameters = settings ? L"--settings" : L"--ai-only";
    info.nShow = SW_SHOWNORMAL;
    if (!ShellExecuteExW(&info)) Wh_Log(L"AI launch failed: %u", GetLastError());
}

static xaml::FrameworkElement FindElement(xaml::DependencyObject const& root,
                                         wchar_t const* name, unsigned depth = 0) {
    if (!root || depth > 32) return nullptr;
    if (auto element = root.try_as<xaml::FrameworkElement>();
        element && element.Name() == name) return element;
    int count = xaml::Media::VisualTreeHelper::GetChildrenCount(root);
    for (int index = 0; index < count; ++index) {
        auto found = FindElement(xaml::Media::VisualTreeHelper::GetChild(root, index), name, depth + 1);
        if (found) return found;
    }
    return nullptr;
}

static void RemoveButton() {
    if (!g_attached) return;
    auto& state = *g_attached;
    if (state.button) {
        state.artwork.ImageOpened(state.artworkOpened);
        state.artwork.ImageFailed(state.artworkFailed);
        state.button.Click(state.click);
        state.button.RightTapped(state.rightTap);
        state.button.LayoutUpdated(state.layout);
        controls::Primitives::FlyoutBase::SetAttachedFlyout(state.button, nullptr);
        uint32_t index;
        if (state.parent.Children().IndexOf(state.button, index)) state.parent.Children().RemoveAt(index);
    }
    slot::Release(state.parent, state.lease);
    delete g_attached;
    g_attached = nullptr;
    Wh_SetIntValue(L"ButtonAttached", 0);
}

static void AttachButton(void* parameter) {
    if (g_unloading) return;
    HWND window = static_cast<HWND>(parameter);
    auto root = taskbar::GetTaskbarXamlRoot(window);
    if (!root) return;
    auto frame = FindElement(root.Content(), L"SystemTrayFrameGrid");
    auto parent = frame ? frame.try_as<controls::Panel>() : nullptr;
    if (!parent) return;
    if (g_attached && g_attached->parent == parent &&
        slot::FindDirectChild(parent, L"AIEnterAiButton")) {
        Wh_SetIntValue(L"ButtonAttached", 1);
        return;
    }
    RemoveButton();
    int position;
    if (!slot::ResolveSlot(parent, slot::Anchor::AfterClock, position)) return;
    controls::Button button;
    button.Name(L"AIEnterAiButton");
    button.MinWidth(0);
    button.Width(28);
    button.Padding({0, 0, 0, 0});
    button.Margin({0, 2, 0, 2});
    button.VerticalAlignment(xaml::VerticalAlignment::Stretch);
    std::wstring asset = g_executable.substr(0, g_executable.find_last_of(L"\\/")) + L"/Assets/AIEnter.png";
    std::replace(asset.begin(), asset.end(), L'\\', L'/');
    xaml::Shapes::Ellipse icon;
    icon.Width(24); icon.Height(24);

    xaml::Media::Imaging::BitmapImage artwork(winrt::Windows::Foundation::Uri(L"file:///" + asset));
    // The supplied 320px canvas contains a 240px sphere centered at (160,160).
    // Crop in the brush, preserving the source bytes and smooth circular edge.
    xaml::Media::ImageBrush fill;
    fill.ImageSource(artwork);
    fill.Stretch(xaml::Media::Stretch::UniformToFill);
    xaml::Media::ScaleTransform crop;
    crop.CenterX(0.5); crop.CenterY(0.5);
    crop.ScaleX(320.0 / 240.0); crop.ScaleY(320.0 / 240.0);
    fill.RelativeTransform(crop);
    icon.Fill(fill);
    controls::Grid glyphHost, glow;
    glyphHost.IsHitTestVisible(false); glow.IsHitTestVisible(false);
    glyphHost.Children().Append(glow); glyphHost.Children().Append(icon);
    button.Content(glyphHost);
    xaml::Automation::AutomationProperties::SetName(button, L"AIEnter AI 对话");
    controls::ToolTipService::SetToolTip(button, winrt::box_value(L"AIEnter AI · 左键打开对话，右键打开设置"));
    auto state = new AttachedButton;
    state->parent = parent;
    state->button = button; state->glyphHost = glyphHost; state->glow = glow; state->icon = icon;
    if (!slot::AcquireAt(parent, position, L"AIEnterAiPosition", state->lease)) { delete state; return; }
    try {
        state->click = button.Click([](auto&&, auto&&) { LaunchAi(); });
        // A direct right-click action avoids keeping callbacks in an open flyout at unload.
        state->rightTap = button.RightTapped([](auto&&, auto&& args) { args.Handled(true); LaunchAi(true); });
        if (!slot::PlaceChild(parent, state->lease, button)) throw winrt::hresult_error(E_FAIL);
    } catch (...) {
        button.Click(state->click); button.RightTapped(state->rightTap);
        slot::Release(parent, state->lease); delete state; throw;
    }
    g_attached = state;
    UpdateButtonAppearance();
    state->artwork = artwork;
    state->artworkOpened = artwork.ImageOpened([](auto&& source, auto&&) {
        Wh_SetIntValue(L"ArtworkLoaded", 1);
        Wh_SetIntValue(L"ArtworkPixels", source.template as<xaml::Media::Imaging::BitmapImage>().PixelWidth());
    });
    state->artworkFailed = artwork.ImageFailed([](auto&&, auto&&) {
        Wh_SetIntValue(L"ArtworkLoaded", 0);
        Wh_Log(L"Taskbar artwork could not be decoded.");
    });
    state->layout = button.LayoutUpdated([state](auto&&, auto&&) {
        try {
            if (state->button.ActualWidth() < 1) return;
            auto clock = slot::FindDirectChild(state->parent, L"NotificationCenterButton");
            if (!clock) return;
            auto buttonPoint = state->button.TransformToVisual(state->parent).TransformPoint({0, 0});
            auto clockPoint = clock.TransformToVisual(state->parent).TransformPoint({0, 0});
            int x = (int)buttonPoint.X, right = (int)(clockPoint.X + clock.ActualWidth()), width = (int)state->button.ActualWidth();
            if (state->lastX == x && state->lastRight == right && state->lastWidth == width) return;
            state->lastX = x; state->lastRight = right; state->lastWidth = width;
            Wh_SetIntValue(L"ButtonWidth", (int)state->button.ActualWidth());
            Wh_SetIntValue(L"ButtonX", (int)buttonPoint.X);
            Wh_SetIntValue(L"ClockX", (int)clockPoint.X);
            double clockRight = clockPoint.X + clock.ActualWidth();
            Wh_SetIntValue(L"ClockRight", (int)clockRight);
            Wh_SetIntValue(L"ButtonAfterClock", clockRight <= buttonPoint.X ? 1 : 0);
            Wh_SetIntValue(L"LayoutDoesNotOverlap", clockRight <= buttonPoint.X ? 1 : 0);
            if (auto desktop = slot::FindDirectChild(state->parent, L"ShowDesktopStack")) {
                auto point = desktop.TransformToVisual(state->parent).TransformPoint({0, 0});
                Wh_SetIntValue(L"ShowDesktopX", (int)point.X);
                Wh_SetIntValue(L"ButtonBeforeDesktop", buttonPoint.X + state->button.ActualWidth() <= point.X ? 1 : 0);
            }
            Wh_SetIntValue(L"ArtworkSize", (int)state->icon.Width());
        } catch (...) { LogFailure(L"Layout validation"); }
    });
    Wh_SetIntValue(L"ButtonAttached", 1);
    Wh_Log(L"Inserted real AI button after clock; slot=%d; layout=%s", position, slot::ClassName(parent).c_str());
}

static void LogFailure(PCWSTR context) {
    try { throw; }
    catch (winrt::hresult_error const& error) { Wh_Log(L"%s: 0x%08X %s", context, (unsigned)error.code().value, error.message().c_str()); }
    catch (...) { Wh_Log(L"%s failed", context); }
}

static void Rebuilt() {
    if (g_refresh && !g_unloading) {
        Wh_SetIntValue(L"ButtonAttached", 0);
        SetEvent(g_refresh);
    }
}

BOOL Wh_ModInit() {
    Wh_SetIntValue(L"ButtonAttached", 0);
    Wh_SetIntValue(L"ArtworkLoaded", 0);
    auto executable = Wh_GetStringSetting(L"executable");
    g_executable = executable; Wh_FreeStringSetting(executable);
    if (g_executable.empty() || GetFileAttributesW(g_executable.c_str()) == INVALID_FILE_ATTRIBUTES) return FALSE;
    dispatch::SetExceptionLogger(LogFailure);
    if (!taskbar::HookTaskbarSymbols(Rebuilt)) return FALSE;
    g_stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_refresh = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    g_appearanceEvent = CreateEventW(nullptr, FALSE, FALSE, L"Local\\AIEnter.TaskbarAppearanceChanged");
    g_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, 32, L"Local\\AIEnter.TaskbarAppearance");
    if (g_mapping) g_shared = static_cast<volatile LONG*>(MapViewOfFile(g_mapping, FILE_MAP_READ, 0, 0, 32));
    if (!g_stop || !g_refresh || !g_appearanceEvent || !g_mapping || !g_shared) {
        if (g_stop) CloseHandle(g_stop);
        if (g_refresh) CloseHandle(g_refresh);
        if (g_shared) UnmapViewOfFile(const_cast<LONG*>(g_shared));
        if (g_mapping) CloseHandle(g_mapping);
        if (g_appearanceEvent) CloseHandle(g_appearanceEvent);
        g_shared = nullptr; g_mapping = g_appearanceEvent = nullptr;
        g_stop = g_refresh = nullptr;
        return FALSE;
    }
    return TRUE;
}

void Wh_ModAfterInit() {
    g_worker = std::thread([] {
        HANDLE watchedProcess = nullptr;
        LONG watchedPid = 0;
        auto apply = [&](AppearanceState state) {
            if (state.pid != watchedPid || !watchedProcess) {
                if (watchedProcess) CloseHandle(watchedProcess);
                watchedProcess = state.pid ? OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, state.pid) : nullptr;
                watchedPid = state.pid;
                WCHAR path[32768]; DWORD length = ARRAYSIZE(path);
                if (watchedProcess && (!QueryFullProcessImageNameW(watchedProcess, 0, path, &length) || _wcsicmp(path, g_executable.c_str()))) {
                    CloseHandle(watchedProcess); watchedProcess = nullptr;
                }
            }
            if (!watchedProcess || WaitForSingleObject(watchedProcess, 0) == WAIT_OBJECT_0) state.open = 0;
            auto window = folder_menus_taskbar_window::FindCurrentProcessTaskbarWnd();
            if (window) dispatch::RunFromWindowThread(window, ApplyAppearanceState, &state, L"AIEnter.TaskbarAi.Dispatch");
        };
        bool reattach = true;
        while (!g_unloading) {
            apply(ReadAppearance());
            if (reattach) {
                for (int attempt = 0; attempt < 40 && !g_unloading; ++attempt) {
                    auto window = folder_menus_taskbar_window::FindCurrentProcessTaskbarWnd();
                    if (window) dispatch::RunFromWindowThread(window, AttachButton, window, L"AIEnter.TaskbarAi.Dispatch");
                    if (Wh_GetIntValue(L"ButtonAttached", 0) == 1) break;
                    if (WaitForSingleObject(g_stop, 1500) == WAIT_OBJECT_0) break;
                }
            }
            HANDLE events[]{g_stop, g_refresh, g_appearanceEvent, watchedProcess};
            DWORD signaled = WaitForMultipleObjects(watchedProcess ? 4 : 3, events, FALSE, INFINITE);
            if (signaled == WAIT_OBJECT_0 || signaled == WAIT_FAILED) break;
            reattach = signaled == WAIT_OBJECT_0 + 1;
            if (signaled == WAIT_OBJECT_0 + 3) {
                AppearanceState state = ReadAppearance(); state.open = 0; apply(state);
                if (watchedProcess) CloseHandle(watchedProcess);
                watchedProcess = nullptr; watchedPid = 0;
                // Wait for a new state or taskbar rebuild after a process exit, without polling.
                HANDLE idle[]{g_stop, g_refresh, g_appearanceEvent};
                signaled = WaitForMultipleObjects(3, idle, FALSE, INFINITE);
                if (signaled == WAIT_OBJECT_0 || signaled == WAIT_FAILED) break;
                reattach = signaled == WAIT_OBJECT_0 + 1;
            }
        }
        if (watchedProcess) CloseHandle(watchedProcess);
    });
}

void Wh_ModUninit() {
    g_unloading = true;
    if (g_stop) SetEvent(g_stop);
    if (g_worker.joinable()) g_worker.join();
    auto window = folder_menus_taskbar_window::FindCurrentProcessTaskbarWnd();
    if (window) dispatch::RunFromWindowThread(window, [](void*) { RemoveButton(); }, nullptr, L"AIEnter.TaskbarAi.Dispatch");
    if (g_stop) CloseHandle(g_stop);
    if (g_refresh) CloseHandle(g_refresh);
    if (g_shared) UnmapViewOfFile(const_cast<LONG*>(g_shared));
    if (g_mapping) CloseHandle(g_mapping);
    if (g_appearanceEvent) CloseHandle(g_appearanceEvent);
    g_shared = nullptr; g_mapping = g_appearanceEvent = nullptr;
    g_stop = g_refresh = nullptr;
}
