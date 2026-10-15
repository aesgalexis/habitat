Option Explicit

Dim shell, files, projectDir, executable, artifactsDir, releaseDir
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")

projectDir = files.GetParentFolderName(WScript.ScriptFullName)
artifactsDir = projectDir & "\artifacts"
releaseDir = ReadRelease("current-release.txt")
If releaseDir = "" Then releaseDir = ReadRelease("previous-release.txt")
If releaseDir = "" Then releaseDir = artifactsDir & "\win-x64"
executable = releaseDir & "\HABITAT.exe"

If Not files.FileExists(executable) Then
    MsgBox "HABITAT is not installed. Run scripts\build-winui.ps1 -Install from Windows.", _
        vbExclamation, "HABITAT"
    WScript.Quit 1
End If

shell.CurrentDirectory = files.GetParentFolderName(executable)
shell.Run """" & executable & """", 1, False

Function ReadRelease(pointerName)
    Dim pointerPath, reader, relative, candidate
    ReadRelease = ""
    pointerPath = artifactsDir & "\" & pointerName
    If Not files.FileExists(pointerPath) Then Exit Function
    On Error Resume Next
    Set reader = files.OpenTextFile(pointerPath, 1)
    If Err.Number <> 0 Then
        Err.Clear
        On Error GoTo 0
        Exit Function
    End If
    relative = Trim(reader.ReadAll)
    reader.Close
    candidate = files.GetAbsolutePathName(artifactsDir & "\" & relative)
    If Err.Number = 0 Then
        If LCase(Left(candidate, Len(artifactsDir) + 1)) = LCase(artifactsDir & "\") Then
            If files.FileExists(candidate & "\HABITAT.exe") Then ReadRelease = candidate
        End If
    End If
    Err.Clear
    On Error GoTo 0
End Function
