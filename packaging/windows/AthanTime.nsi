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
OutFile "${OUTPUT_DIR}\AthanTime-${APP_VERSION}-Windows-x64-Setup.exe"
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
Var NoStart
Var UserAppArguments
Var UserAppWait
Var UserAppResult
Var UserAppError
Var CleanupAttempts
Var RemoveSettingsChoice
Var UninstallRequested
Var UninstallRadio
Var InternalRelay

Page custom ScopePage ScopePageLeave
Page custom MaintenancePage MaintenancePageLeave
Page directory DirectoryPre DirectoryShow DirectoryLeave
DirText "Setup will install Athan Time in the folder below. Fresh installations can choose another folder. Updates retain their registered folder."
Page instfiles
UninstPage uninstConfirm
UninstPage custom un.SettingsPage un.SettingsPageLeave
UninstPage instfiles

Function .onInit
  StrCpy $InstallScope "CurrentUser"
  StrCpy $ScopeRequested ""
  StrCpy $RequestedDirectory $INSTDIR
  ${GetParameters} $0
  StrCpy $InternalRelay "0"
  ClearErrors
  ${GetOptions} $0 "/InternalRelay" $1
  IfErrors +2
  StrCpy $InternalRelay "1"
  StrCpy $UninstallRequested "0"
  ClearErrors
  ${GetOptions} $0 "/Uninstall" $1
  IfErrors +2
  StrCpy $UninstallRequested "1"
  StrCpy $NoStart ""
  ClearErrors
  ${GetOptions} $0 "/NoStart" $1
  IfErrors +2
  StrCpy $NoStart "1"
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
  StrCmp $UninstallRequested "1" 0 +2
  Call RunRegisteredUninstaller
  ; Interactive setup confirms scope before any elevation or write-access check.
  IfSilent applyScope
  Goto done
applyScope:
  Call ValidateTargetScope
  Call RequireMachinePrivileges
done:
FunctionEnd

Function MaintenancePage
  ; The parent already chose Install/update before starting this elevated child.
  StrCmp $InternalRelay "1" 0 +2
  Abort
  StrCmp $ExistingInstallDirectory "" 0 +2
  Abort
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 32u "Athan Time is installed in the selected scope. Choose an action."
  Pop $0
  ${NSD_CreateRadioButton} 0 40u 100% 20u "Install or update Athan Time"
  Pop $0
  ${NSD_Check} $0
  ${NSD_CreateRadioButton} 0 68u 100% 20u "Uninstall this installation"
  Pop $UninstallRadio
  nsDialogs::Show
FunctionEnd

Function MaintenancePageLeave
  ${NSD_GetState} $UninstallRadio $0
  StrCmp $0 ${BST_CHECKED} 0 done
  Call RunRegisteredUninstaller
done:
FunctionEnd

Function RunRegisteredUninstaller
  StrCmp $ExistingInstallDirectory "" missing
  Call ValidateTargetScope
  IfFileExists "$INSTDIR\Uninstall.exe" 0 missing
  ; A temporary copy can delete the installed uninstaller and return its real result.
  InitPluginsDir
  ClearErrors
  CopyFiles /SILENT "$INSTDIR\Uninstall.exe" "$PLUGINSDIR\AthanRemove.exe"
  IfErrors missing
  StrCpy $1 ""
  IfSilent 0 +2
  StrCpy $1 "/S "
  ExecWait '"$PLUGINSDIR\AthanRemove.exe" $1_?=$INSTDIR' $0
  IfErrors missing
  SetErrorLevel $0
  Quit
missing:
  MessageBox MB_OK|MB_ICONSTOP "No registered uninstaller is available for this scope. Install or repair this copy first." /SD IDOK
  SetErrorLevel 2
  Quit
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
  ; The original unelevated parent owns the post-install launch.
  StrCpy $2 "/AllUsers /NoStart /InternalRelay"
  IfSilent 0 +2
  StrCpy $2 "$2 /S"
  StrCpy $2 "$2 /D=$INSTDIR"
  HideWindow
  Call RunElevated
  StrCmp $0 0 0 relayDone
  ; The elevated directory page can change a fresh install's destination.
  ReadRegStr $1 HKLM "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $1 "" relayDone
  StrCpy $INSTDIR $1
  Call LaunchInstalledApp
relayDone:
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
  ; Choose maintenance while still unelevated; only installation needs UAC here.
  Call RequireMachinePrivileges
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

!macro DefineCloseRunningApp PREFIX
Function ${PREFIX}CloseRunningApp
  IfFileExists "$INSTDIR\AthanTime.Net.exe" 0 done
  !if "${PREFIX}" == "un."
    ; User commands run in the original parent. The removal child only needs RM.
    UserInfo::GetAccountType
    Pop $0
    StrCmp $0 "Admin" restartManager
  !endif
  ; Updated apps accept same-user IPC even while hidden in the tray. Old apps use RM.
  IfFileExists "$INSTDIR\Data\desktop-control.json" 0 restartManager
  StrCpy $UserAppArguments "--shutdown"
  StrCpy $UserAppWait "1"
  Call ${PREFIX}RunUserApp
  DetailPrint "Application shutdown command returned $UserAppResult $UserAppError"
restartManager:
  System::Store "s"
  ; Register only this installation. Never kill by process name or force shutdown.
  System::StrAlloc 33
  Pop $1
  System::Call 'rstrtmgr::RmStartSession(*i.r0, i 0, p r1) i.r2'
  StrCmp $2 0 0 freeKey
  System::Call '*(&t1024 "$INSTDIR\AthanTime.Net.exe") p.r3'
  System::Call '*(p r3) p.r4'
  System::Call 'rstrtmgr::RmRegisterResources(i r0, i 1, p r4, i 0, p 0, i 0, p 0) i.r2'
  StrCmp $2 0 0 endSession
  DetailPrint "Requesting graceful exit of the installed Athan Time..."
  System::Call 'rstrtmgr::RmShutdown(i r0, i 0, p 0) i.r2'
  DetailPrint "Restart Manager shutdown result: $2"
endSession:
  System::Call 'rstrtmgr::RmEndSession(i r0)'
  System::Free $4
  System::Free $3
freeKey:
  System::Free $1
  System::Store "l"
done:
FunctionEnd

!macroend
!insertmacro DefineCloseRunningApp ""
!insertmacro DefineCloseRunningApp "un."

Function .onInstSuccess
  Call LaunchInstalledApp
FunctionEnd

Function LaunchInstalledApp
  IfSilent done
  IfFileExists "$INSTDIR\AthanTime.Net.exe" 0 done
  StrCmp $NoStart "1" done
  StrCpy $UserAppArguments "--show-window"
  StrCpy $UserAppWait "0"
  Call RunUserApp
  StrCmp $UserAppResult "0" done
  MessageBox MB_OK|MB_ICONINFORMATION "Athan Time is installed. Open it from the Start menu; setup could not start it automatically." /SD IDOK
done:
FunctionEnd

; User commands share one launcher. Uninstall cleanup requires the original
; unelevated parent; only setup completion may use a desktop-token fallback.
!macro DefineRunUserApp PREFIX
Function ${PREFIX}RunUserApp
  System::Store "s"
  StrCpy $UserAppResult "-1"
  StrCpy $UserAppError "Starting the application"
  UserInfo::GetAccountType
  Pop $0
  StrCmp $0 "Admin" desktopUser
  ClearErrors
  StrCmp $UserAppWait "1" waitUser
  ExecShell "open" "$INSTDIR\AthanTime.Net.exe" "$UserAppArguments"
  IfErrors done
  StrCpy $UserAppResult "0"
  StrCpy $UserAppError ""
  Goto done
waitUser:
  ExecWait '"$INSTDIR\AthanTime.Net.exe" $UserAppArguments' $0
  IfErrors done
  StrCpy $UserAppResult $0
  StrCpy $UserAppError ""
  Goto done
desktopUser:
  !if "${PREFIX}" == "un."
    StrCpy $UserAppError "Start uninstall normally, without Run as administrator, to remove your settings."
    Goto done
  !else
  StrCpy $UserAppError "No signed-in desktop shell is available"
  System::Call 'user32::GetShellWindow() p.r1'
  StrCmp $1 0 done
  System::Call 'user32::GetWindowThreadProcessId(p r1, *i.r0) i.r9'
  StrCmp $9 0 done
  System::Call 'kernel32::OpenProcess(i 0x1000, i 0, i r0) p.r2 ?e'
  Pop $9
  StrCpy $UserAppError "Opening desktop process (Windows error $9)"
  StrCmp $2 0 done
  System::Call 'advapi32::OpenProcessToken(p r2, i 0xB, *p.r3) i.r0 ?e'
  Pop $9
  StrCpy $UserAppError "Opening desktop-user token (Windows error $9)"
  StrCmp $0 0 closeProcess
  System::Call 'userenv::CreateEnvironmentBlock(*p.r4, p r3, i 0) i.r0 ?e'
  Pop $9
  StrCpy $UserAppError "Creating desktop-user environment (Windows error $9)"
  StrCmp $0 0 closeToken
  ; Structures match the standard 32-bit Unicode NSIS stub.
  System::Call '*(i 68, i 0, p 0, p 0, i 0, i 0, i 0, i 0, i 0, i 0, i 0, i 0, i 0, p 0, p 0, p 0, p 0) p.r5'
  System::Call '*(p 0, p 0, i 0, i 0) p.r6'
  System::Call `advapi32::CreateProcessWithTokenW(p r3, i 1, w '$INSTDIR\AthanTime.Net.exe', w '"$INSTDIR\AthanTime.Net.exe" $UserAppArguments', i 0x400, p r4, w '$INSTDIR', p r5, p r6) i.r0 ?e`
  Pop $9
  StrCpy $UserAppError "Starting as desktop user (Windows error $9)"
  StrCmp $0 0 freeStructures
  System::Call '*$6(p.r7, p.r8, i, i)'
  StrCpy $UserAppResult "0"
  StrCpy $UserAppError ""
  StrCmp $UserAppWait "1" 0 closeChild
  StrCpy $UserAppResult "-1"
  StrCpy $UserAppError "Waiting for application command (30-second timeout)"
  System::Call 'kernel32::WaitForSingleObject(p r7, i 30000) i.r0'
  StrCmp $0 0 0 closeChild
  System::Call 'kernel32::GetExitCodeProcess(p r7, *i.r0) i.r1 ?e'
  Pop $9
  StrCpy $UserAppError "Reading application result (Windows error $9)"
  StrCmp $1 0 closeChild
  StrCpy $UserAppResult $0
  StrCpy $UserAppError ""
closeChild:
  System::Call 'kernel32::CloseHandle(p r7)'
  System::Call 'kernel32::CloseHandle(p r8)'
freeStructures:
  System::Free $6
  System::Free $5
  System::Call 'userenv::DestroyEnvironmentBlock(p r4)'
closeToken:
  System::Call 'kernel32::CloseHandle(p r3)'
closeProcess:
  System::Call 'kernel32::CloseHandle(p r2)'
  !endif
done:
  System::Store "l"
FunctionEnd
!macroend
!insertmacro DefineRunUserApp ""
!insertmacro DefineRunUserApp "un."

Function un.SettingsPage
  StrCpy $RemoveSettingsChoice "0"
  nsDialogs::Create 1018
  Pop $0
  ${NSD_CreateLabel} 0 0 100% 44u "Keep settings by default. Optional cleanup removes this user's preferences and app logs before All users removal requests administrator permission. Other users and custom recordings are kept."
  Pop $0
  ${NSD_CreateCheckbox} 0 52u 100% 28u "Remove my settings (currently signed-in desktop user only)"
  Pop $RemoveSettingsChoice
  UserInfo::GetAccountType
  Pop $0
  StrCmp $0 "Admin" 0 showPage
  EnableWindow $RemoveSettingsChoice 0
  ${NSD_CreateLabel} 0 90u 100% 40u "To remove your settings, close this uninstaller and start it normally, without Run as administrator. This elevated run keeps settings."
  Pop $0
showPage:
  nsDialogs::Show
FunctionEnd

Function un.SettingsPageLeave
  ${NSD_GetState} $RemoveSettingsChoice $RemoveSettingsChoice
FunctionEnd

Function un.RemoveMySettings
  ; Silent uninstall keeps settings. No all-profile purge or silent destructive switch.
  IfSilent done
  StrCmp $RemoveSettingsChoice ${BST_CHECKED} 0 done
retry:
  ; Never attempt token-based cleanup or delete an elevated administrator's profile.
  UserInfo::GetAccountType
  Pop $0
  StrCmp $0 "Admin" 0 closeUserApp
  StrCpy $UserAppResult "-1"
  StrCpy $UserAppError "Restart uninstall normally, without Run as administrator, to remove your settings."
  Goto showFailure
closeUserApp:
  Call un.CloseRunningApp
  StrCpy $UserAppArguments "--remove-user-settings"
  StrCpy $UserAppWait "1"
  StrCpy $CleanupAttempts "0"
runCleanup:
  Call un.RunUserApp
  ; File access may be released just before the shutting-down process releases its lease.
  StrCmp $UserAppError "" 0 showFailure
  StrCmp $UserAppResult "1" 0 evaluateResult
  IntOp $CleanupAttempts $CleanupAttempts + 1
  IntCmp $CleanupAttempts 20 evaluateResult waitBusy evaluateResult
waitBusy:
  Sleep 250
  Goto runCleanup
evaluateResult:
  StrCmp $UserAppResult "0" done
  StrCmp $UserAppError "" 0 showFailure
  StrCpy $UserAppError "Cleanup returned exit code $UserAppResult"
  ${Switch} $UserAppResult
    ${Case} 1
      StrCpy $UserAppError "Athan Time is still running, possibly from another installation or session. Exit it and Retry."
      ${Break}
    ${Case} 2
      StrCpy $UserAppError "The installed app rejected the cleanup command. Repair/update this installation before uninstalling."
      ${Break}
    ${Case} 3
      StrCpy $UserAppError "The OS did not provide an absolute user-profile folder."
      ${Break}
    ${Case} 4
      StrCpy $UserAppError "An app file or login-startup entry could not be removed. Check locks and access, then Retry."
      ${Break}
    ${Case} 5
      StrCpy $UserAppError "Cleanup refused a linked profile, log folder or file. No profile files were deleted by this attempt."
      ${Break}
    ${Case} 6
      StrCpy $UserAppError "Access was denied to this user's app files or login-startup entry."
      ${Break}
  ${EndSwitch}
showFailure:
  DetailPrint "Settings cleanup failed: $UserAppError; result $UserAppResult"
  MessageBox MB_ABORTRETRYIGNORE|MB_ICONEXCLAMATION|MB_DEFBUTTON2 "Settings removal could not finish.$\r$\n$\r$\n$UserAppError$\r$\n$\r$\nResult: $UserAppResult.$\r$\nRetry tries settings removal again.$\r$\nIgnore keeps any remaining settings and continues uninstall.$\r$\nAbort stops uninstall.$\r$\nOther users and custom recordings are kept." IDRETRY retry IDIGNORE done
  SetErrorLevel 2
  Abort
done:
FunctionEnd
!macro DefineEnsureAppClosed PREFIX
Function ${PREFIX}EnsureAppClosed
retry:
  StrCpy $9 0
check:
  IfFileExists "$INSTDIR\AthanTime.Net.exe" 0 done
  ClearErrors
  ; Append mode checks write access without truncating or changing the executable.
  FileOpen $0 "$INSTDIR\AthanTime.Net.exe" a
  IfErrors busy
  FileClose $0
  Goto done
busy:
  IntOp $9 $9 + 1
  IntCmp $9 40 prompt wait prompt
wait:
  Sleep 250
  Goto check
prompt:
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
  ; Keep the original user's UI and cleanup before elevation. Only the child
  ; that removes shared files needs write access to Program Files.
  UserInfo::GetAccountType
  Pop $0
  StrCmp $0 "Admin" ready userReady
current:
  SetShellVarContext current
  SetRegView 32
ready:
  Call un.CloseRunningApp
  Call un.EnsureAppClosed
  Return
userReady:
  Call un.CloseRunningApp
FunctionEnd

Function un.RequireMachinePrivileges
  StrCmp $InstallScope "AllUsers" 0 done
  UserInfo::GetAccountType
  Pop $0
  StrCmp $0 "Admin" done
  ; Elevate a temporary copy: the child can delete the installed uninstaller while
  ; the parent waits for its real exit code. _?= prevents an extra detached copy.
  InitPluginsDir
  ClearErrors
  CopyFiles /SILENT "$EXEPATH" "$PLUGINSDIR\AthanUninstall.exe"
  IfErrors failed
  StrCpy $1 "$PLUGINSDIR\AthanUninstall.exe"
  ; Confirmation and optional user cleanup already finished in this parent.
  StrCpy $2 "/S _?=$INSTDIR"
  HideWindow
  Call un.RunElevated
  ; The silent child cannot notify the user. Only its successful parent does.
  StrCmp $0 0 0 finished
  Call un.onUninstSuccess
finished:
  SetErrorLevel $0
  Quit
failed:
  SetErrorLevel 2
  Quit
done:
FunctionEnd

Function un.onUninstSuccess
  ; Used by normal removal and the original parent after successful elevation.
  ; Silent removals, cancelled UAC and failed children never display success.
  IfSilent done
  MessageBox MB_OK|MB_ICONINFORMATION "Athan Time was uninstalled successfully."
done:
FunctionEnd

Section "Athan Time" MainSection
  Call ValidateTargetScope
  Call RequireMachinePrivileges
  StrCmp $ExistingInstallDirectory "" +2
  StrCpy $INSTDIR $ExistingInstallDirectory
  Call CloseRunningApp
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
  WriteRegStr SHCTX "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/Cophla/AthanTime.Net"
  WriteRegStr SHCTX "${UNINSTALL_KEY}" "URLUpdateInfo" "https://github.com/Cophla/AthanTime.Net/releases"
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
  Call un.CloseRunningApp
  Call un.RemoveMySettings
  Call un.RequireMachinePrivileges
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
