Set fs = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
base = fs.GetParentFolderName(WScript.ScriptFullName)
shell.Run Chr(34) & base & "\TaskbarRuntime\windhawk.exe" & Chr(34) & " -tray-only", 0, False
shell.Run Chr(34) & base & "\AIEnter.exe" & Chr(34), 1, False
