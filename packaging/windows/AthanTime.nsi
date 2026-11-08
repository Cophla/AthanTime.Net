!ifndef APP_VERSION
  !error "Build through the packaging recipe to supply the shared APP_VERSION."
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\..\artifacts\publish\win-x64"
!endif
!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "..\..\artifacts\packages"
!endif

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\AthanTime"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "${UNINSTALL_INCLUDE}"

; Sign the actual uninstaller before NSIS embeds it, including its runtime temp copy.
!ifdef SIGN_SCRIPT
  !uninstfinalize 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "${SIGN_SCRIPT}" -Path "%1"' = 0
!endif

Unicode True
; Preserve installation identity/paths while exposing the full prerelease version in setup/uninstall.
Name "Athan Time ${APP_VERSION}"
OutFile "${OUTPUT_DIR}\AthanTime-${APP_VERSION}-win-x64-setup.exe"
; Leave the header default empty so .onInit can distinguish a native /D= override.
; NSIS removes /D= from $CMDLINE; ConfigureInstallScope supplies both defaults.
InstallDir ""
; Elevate only after All users is selected, keeping Only me usable without admin rights.
RequestExecutionLevel user
SetCompressor /SOLID lzma
AllowSkipFiles off

Var ExistingInstallDirectory
Var InstallScope
Var ScopeRequested
Var RequestedDirectory
Var ScopeDialog
Var CurrentUserRadio
Var AllUsersRadio

Page custom ScopePage ScopePageLeave
Page directory DirectoryPre DirectoryShow DirectoryLeave
DirText "Setup will install Athan Time in the folder below. Fresh installations can choose another folder. Updates retain their registered folder."
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Function .onInit
  StrCpy $InstallScope "CurrentUser"
  StrCpy $ScopeRequested ""
  StrCpy $RequestedDirectory $INSTDIR
  ${GetParameters} $0
  ; Retain existing scope automatically; prefer this user's copy when both exist.
  SetRegView 64
  ReadRegStr $1 HKLM "${UNINSTALL_KEY}" "UninstallString"
  StrCmp $1 "" +2
  StrCpy $InstallScope "AllUsers"
  SetRegView 32
  ReadRegStr $1 HKCU "${UNINSTALL_KEY}" "UninstallString"
  StrCmp $1 "" +2
  StrCpy $InstallScope "CurrentUser"
  ClearErrors
  ${GetOptions} $0 "/CurrentUser" $1
  IfErrors +3
  StrCpy $InstallScope "CurrentUser"
  StrCpy $ScopeRequested "CurrentUser"
  ClearErrors
  ${GetOptions} $0 "/AllUsers" $1
  IfErrors scopeReady
  StrCmp $ScopeRequested "" +4
  MessageBox MB_OK|MB_ICONSTOP "Choose either /CurrentUser or /AllUsers." /SD IDOK
  SetErrorLevel 2
  Quit
  StrCpy $InstallScope "AllUsers"
  StrCpy $ScopeRequested "AllUsers"
scopeReady:
  Call ConfigureInstallScope
  ; Interactive setup confirms scope before any elevation or write-access check.
  IfSilent applyScope
  StrCmp $ScopeRequested "" done
applyScope:
  Call ValidateTargetScope
  Call RequireMachinePrivileges
  Call EnsureAppClosed
done:
FunctionEnd

; Read-only preparation: write-access checks must wait for scope confirmation/elevation.
Function ConfigureInstallScope
  StrCmp $InstallScope "AllUsers" machine
  SetShellVarContext current
  SetRegView 32
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Athan Time"
  Goto readExisting
machine:
  SetShellVarContext all
  SetRegView 64
  StrCpy $INSTDIR "$PROGRAMFILES64\Athan Time"
readExisting:
  StrCmp $RequestedDirectory "" +2
  StrCpy $INSTDIR $RequestedDirectory
  StrCpy $ExistingInstallDirectory ""
  ReadRegStr $0 SHCTX "${UNINSTALL_KEY}" "UninstallString"
  StrCmp $0 "" done
  ReadRegStr $ExistingInstallDirectory SHCTX "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $ExistingInstallDirectory "" legacy locationReady
legacy:
  ; The original recipe wrote a quoted executable path, without arguments.
  StrCpy $1 $0 1
  StrCmp $1 '$\"' 0 +2
  StrCpy $0 $0 -1 1
  ${GetParent} $0 $ExistingInstallDirectory
locationReady:
  ; A command-line /D must not move an update away from its registered folder.
  StrCpy $INSTDIR $ExistingInstallDirectory
done:
FunctionEnd

Function ScopePage
  StrCmp $ScopeRequested "" +2
  Abort
  nsDialogs::Create 1018
  Pop $ScopeDialog
  ${NSD_CreateLabel} 0 0 100% 32u "Who should be able to use Athan Time on this computer?"
  Pop $0
  ${NSD_CreateRadioButton} 10u 40u 90% 14u "Only me (no administrator permission required)"
  Pop $CurrentUserRadio
  ${NSD_CreateRadioButton} 10u 64u 90% 14u "All users (requires administrator permission)"
  Pop $AllUsersRadio
  ${If} $InstallScope == "AllUsers"
    ${NSD_Check} $AllUsersRadio
  ${Else}
    ${NSD_Check} $CurrentUserRadio
  ${EndIf}
  ${NSD_CreateLabel} 0 94u 100% 40u "Existing copies keep their registered folders. The two scopes can coexist in different folders; settings and login startup remain per-user."
  Pop $0
  nsDialogs::Show
FunctionEnd

Function ScopePageLeave
  ${NSD_GetState} $AllUsersRadio $0
  StrCpy $InstallScope "CurrentUser"
  StrCmp $0 ${BST_CHECKED} 0 +2
  StrCpy $InstallScope "AllUsers"
  Call ConfigureInstallScope
  Call ValidateTargetScope
  Call RequireMachinePrivileges
  Call EnsureAppClosed
FunctionEnd

Function ValidateTargetScope
  ; A trailing separator treats the candidate as a directory. Without it, NSIS
  ; requires the final component to exist and clears fresh installation paths.
  ; Normalize into scratch storage so failure cannot erase the selected value.
  ClearErrors
  GetFullPathName $0 "$INSTDIR\"
  IfErrors blocked
  StrCmp $0 "" blocked
  ${GetRoot} $0 $1
  StrCmp $0 "$1\" blocked
  StrCpy $INSTDIR $0 -1
  ReadINIStr $0 "$INSTDIR\athan-install.ini" "Install" "Scope"
  StrCmp $0 "" legacy
  StrCmp $0 $InstallScope done blocked
legacy:
  ; Only a registered legacy update may reuse an unmarked application folder.
  IfFileExists "$INSTDIR\AthanTime.Net.exe" 0 done
  StrCmp $ExistingInstallDirectory $INSTDIR done
blocked:
  MessageBox MB_OK|MB_ICONSTOP "This folder belongs to another or an unregistered Athan Time installation. Choose a different folder or uninstall that copy first." /SD IDOK
  SetErrorLevel 2
  Abort
done:
FunctionEnd

; SHCTX follows the selected shell context, independently in installer/uninstaller.
Function RequireMachinePrivileges
  StrCmp $InstallScope "AllUsers" 0 done
  UserInfo::GetAccountType
  Pop $0
  StrCmp $0 "Admin" done
  StrCpy $1 "$EXEPATH"
  StrCpy $2 "/AllUsers"
  IfSilent 0 +2
  StrCpy $2 "$2 /S"
  StrCpy $2 "$2 /D=$INSTDIR"
  HideWindow
  Call RunElevated
  SetErrorLevel $0
  Quit
done:
FunctionEnd

; NSIS's ExecShellWait cannot return the child's exit code. Preserve silent setup
; failures and UAC cancellation through the bundled System plug-in/Windows API.
!macro DefineRunElevated PREFIX
Function ${PREFIX}RunElevated
  !if ${NSIS_PTR_SIZE} != 4
    !error "The Windows package uses the standard 32-bit Unicode NSIS stub."
  !endif
  System::Call '*(i 60, i 0x140, p $HWNDPARENT, t "runas", t r1, t r2, p 0, i 1, p 0, p 0, i 0, i 0, i 0, p 0, p 0) p.r3'
  ; Capture LastError in the same API call; later plug-in calls can overwrite it.
  System::Call 'shell32::ShellExecuteExW(p r3) i.r0 ?e'
  Pop $5
  StrCmp $0 0 failed
  System::Call '*$3(i, i, p, p, p, p, p, i, p, p, i, i, i, p, p.r4)'
  StrCmp $4 0 failed
  System::Call 'kernel32::WaitForSingleObject(p r4, i -1) i.r0'
  StrCmp $0 0 +3
  StrCpy $0 2
  Goto closeHandle
  System::Call 'kernel32::GetExitCodeProcess(p r4, *i.r0) i.r5'
  StrCmp $5 0 0 closeHandle
  StrCpy $0 2
closeHandle:
  System::Call 'kernel32::CloseHandle(p r4)'
  Goto done
failed:
  StrCpy $0 $5
  StrCmp $0 0 0 done
  StrCpy $0 2
done:
  System::Free $3
FunctionEnd
!macroend
!insertmacro DefineRunElevated ""
!insertmacro DefineRunElevated "un."

Function DirectoryPre
  ; Keep the destination page visible for both scopes, including updates.
  StrCmp $ExistingInstallDirectory "" done
  StrCpy $INSTDIR $ExistingInstallDirectory
done:
FunctionEnd

Function DirectoryShow
  ; Standard NSIS directory control IDs, also used by its bundled MUI directory page.
  FindWindow $0 "#32770" "" $HWNDPARENT
  GetDlgItem $1 $0 1019
  GetDlgItem $2 $0 1001
  ${If} $ExistingInstallDirectory == ""
    ; Reset both controls when Back changes from an update scope to a fresh scope.
    SendMessage $1 ${EM_SETREADONLY} 0 0
    EnableWindow $2 1
  ${Else}
    ; Read-only still permits selecting/copying the retained destination.
    SendMessage $1 ${EM_SETREADONLY} 1 0
    EnableWindow $2 0
  ${EndIf}
FunctionEnd

Function DirectoryLeave
  ; Defend the registered path even if a control or command-line value changes it.
  StrCmp $ExistingInstallDirectory "" +2
  StrCpy $INSTDIR $ExistingInstallDirectory
FunctionEnd

!macro DefineEnsureAppClosed PREFIX
Function ${PREFIX}EnsureAppClosed
retry:
  IfFileExists "$INSTDIR\AthanTime.Net.exe" 0 done
  ClearErrors
  ; Append mode checks write access without truncating or changing the executable.
  FileOpen $0 "$INSTDIR\AthanTime.Net.exe" a
  IfErrors busy
  FileClose $0
  Goto done
busy:
  IfSilent blocked
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION \
    "Setup cannot change Athan Time files. Exit all running copies (including other users' tray icons), check folder write access, then Retry." IDRETRY retry
blocked:
  SetErrorLevel 2
  Abort
done:
FunctionEnd
!macroend
!insertmacro DefineEnsureAppClosed ""
!insertmacro DefineEnsureAppClosed "un."

Function un.onInit
  ; The marker belongs to this directory, so stale uninstallers retain their scope.
  ; Legacy installers have no marker and are always current-user installations.
  ReadINIStr $InstallScope "$INSTDIR\athan-install.ini" "Install" "Scope"
  StrCmp $InstallScope "" 0 +2
  StrCpy $InstallScope "CurrentUser"
  StrCmp $InstallScope "CurrentUser" current
  StrCmp $InstallScope "AllUsers" machine
  SetErrorLevel 2
  Quit
machine:
  SetShellVarContext all
  SetRegView 64
  UserInfo::GetAccountType
  Pop $0
  StrCmp $0 "Admin" ready
  ; Elevate a temporary copy: the child can delete the installed uninstaller while
  ; the parent waits for its real exit code. _?= prevents an extra detached copy.
  InitPluginsDir
  ClearErrors
  CopyFiles /SILENT "$EXEPATH" "$PLUGINSDIR\AthanUninstall.exe"
  IfErrors failed
  StrCpy $1 "$PLUGINSDIR\AthanUninstall.exe"
  StrCpy $2 ""
  IfSilent 0 +2
  StrCpy $2 "/S "
  StrCpy $2 "$2_?=$INSTDIR"
  HideWindow
  Call un.RunElevated
  SetErrorLevel $0
  Quit
failed:
  SetErrorLevel 2
  Quit
current:
  SetShellVarContext current
  SetRegView 32
ready:
  Call un.EnsureAppClosed
FunctionEnd

Section "Athan Time" MainSection
  Call ValidateTargetScope
  Call RequireMachinePrivileges
  StrCmp $ExistingInstallDirectory "" +2
  StrCpy $INSTDIR $ExistingInstallDirectory
  Call EnsureAppClosed
  SetOverwrite on
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*"
  CreateDirectory "$SMPROGRAMS\Athan Time"
  CreateShortcut "$SMPROGRAMS\Athan Time\Athan Time.lnk" "$INSTDIR\AthanTime.Net.exe"
  ClearErrors
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteINIStr "$INSTDIR\athan-install.ini" "Install" "Scope" "$InstallScope"
  IfErrors 0 +3
  SetErrorLevel 2
  Abort
  WriteRegStr SHCTX "${UNINSTALL_KEY}" \
    "DisplayName" "Athan Time ${APP_VERSION}"
  WriteRegStr SHCTX "${UNINSTALL_KEY}" \
    "DisplayVersion" "${APP_VERSION}"
  WriteRegStr SHCTX "${UNINSTALL_KEY}" \
    "InstallLocation" "$INSTDIR"
  WriteRegStr SHCTX "${UNINSTALL_KEY}" \
    "DisplayIcon" "$INSTDIR\AthanTime.Net.exe"
  WriteRegStr SHCTX "${UNINSTALL_KEY}" \
    "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr SHCTX "${UNINSTALL_KEY}" \
    "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
SectionEnd

Section "Uninstall"
  Call un.EnsureAppClosed
  ; Machine uninstall must never edit an admin's or another user's login choices.
  StrCmp $InstallScope "CurrentUser" 0 registration
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "AthanTime"
  StrCmp $0 '"$INSTDIR\AthanTime.Net.exe" --start-minimized' 0 +2
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "AthanTime"
registration:
  ; A stale uninstaller must not remove another installation's registration/shortcut.
  ReadRegStr $0 SHCTX "${UNINSTALL_KEY}" "UninstallString"
  StrCmp $0 '$\"$INSTDIR\Uninstall.exe$\"' 0 removePayload
  Delete "$SMPROGRAMS\Athan Time\Athan Time.lnk"
  RMDir "$SMPROGRAMS\Athan Time"
  DeleteRegKey SHCTX "${UNINSTALL_KEY}"
removePayload:
  !insertmacro RemovePublishedFiles
  Delete "$INSTDIR\athan-install.ini"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
