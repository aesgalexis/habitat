Option Explicit

Dim shell, files, projectDir, executable
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")

projectDir = files.GetParentFolderName(WScript.ScriptFullName)
executable = projectDir & "\artifacts\win-x64\HABITAT.exe"

If Not files.FileExists(executable) Then
    MsgBox "HABITAT todavía no está compilado. Ejecuta scripts\build-winui.ps1 una vez.", _
        vbExclamation, "HABITAT"
    WScript.Quit 1
End If

shell.CurrentDirectory = files.GetParentFolderName(executable)
shell.Run """" & executable & """", 1, False
