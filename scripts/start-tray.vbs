Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
root = fso.GetParentFolderName(base)
exe = root & "\dist\PadKeyServer.exe"
Set sh = CreateObject("WScript.Shell")
sh.Run Chr(34) & exe & Chr(34) & " --tray", 7, False
