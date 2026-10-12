Option Explicit

Dim shell, files, projectDir, command
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")

projectDir = files.GetParentFolderName(WScript.ScriptFullName)
shell.CurrentDirectory = projectDir
command = "pyw.exe -3 """ & projectDir & "\app.py"""

On Error Resume Next
shell.Run command, 0, False
If Err.Number <> 0 Then
    Err.Clear
    command = "pythonw.exe """ & projectDir & "\app.py"""
    shell.Run command, 0, False
End If
On Error GoTo 0
