; G-EQ — All-in-one Installer
; Installs EqualizerAPO (downloads at install time) then G-EQ.
;
; Requirements:
;   NSIS 3.x          https://nsis.sourceforge.io/Download
;   inetc plugin      vendored in nsis-plugins/x86-unicode/INetC.dll (no
;                      Program Files write access needed to build)
;
; Build: makensis installer.nsi

Unicode True

!addplugindir /x86-unicode "nsis-plugins\x86-unicode"

!define APP_NAME       "G-EQ"
!define APP_EXE        "GamingEqualizer.exe"
!define APP_VERSION    "3.0.0"
!define PUBLISHER      "ReDCLiF"
!define INSTALL_DIR    "$PROGRAMFILES64\GEqualizer"
!define REG_UNINSTALL  "Software\Microsoft\Windows\CurrentVersion\Uninstall\GEqualizer"
!define EQAPO_REG      "SOFTWARE\EqualizerAPO"
!define EQAPO_URL      "https://downloads.sourceforge.net/project/equalizerapo/1.3/EqualizerAPO64-1.3.exe"
!define EQAPO_TMP      "$TEMP\EqualizerAPO64-1.3.exe"

Name          "${APP_NAME} Setup"
OutFile       "G-EQ-Setup-${APP_VERSION}.exe"
InstallDir    "${INSTALL_DIR}"
Icon          "app\app-icon.ico"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText  "${APP_NAME} ${APP_VERSION}"

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MUI_ICON               "app\app-icon.ico"
!define MUI_UNICON             "app\app-icon.ico"
!define MUI_ABORTWARNING

; Pages
!insertmacro MUI_PAGE_WELCOME
Page custom EqualizerAPOPage
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN          "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT     "Launch G-EQ"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Reboot now (required after EqualizerAPO install)"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION RequestReboot
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ── Custom page: EqualizerAPO notice ─────────────────────────────────────────

Var EqApoInstalled

Function EqualizerAPOPage
    ; Check if EqualizerAPO is already installed
    ReadRegStr $0 HKLM "${EQAPO_REG}" "InstallPath"
    ${If} $0 != ""
        StrCpy $EqApoInstalled "1"
        Abort   ; skip the page — already installed
    ${EndIf}
    StrCpy $EqApoInstalled "0"

    nsDialogs::Create 1018
    Pop $0

    ${NSD_CreateLabel} 0 0 100% 20u "EqualizerAPO — Required Audio Engine"
    Pop $0
    SetCtlColors $0 0x00FF88 transparent
    CreateFont $1 "Segoe UI" 11 700
    SendMessage $0 ${WM_SETFONT} $1 0

    ${NSD_CreateLabel} 0 28u 100% 80u \
        "G-EQ requires EqualizerAPO to control your system audio.$\r$\n$\r$\nThe installer will now download and install EqualizerAPO automatically.$\r$\n$\r$\nDuring the EqualizerAPO setup, select the audio output device you use for gaming.$\r$\n$\r$\nA reboot will be required after EqualizerAPO installs."
    Pop $0

    nsDialogs::Show
FunctionEnd

; ── Install ───────────────────────────────────────────────────────────────────

Section "EqualizerAPO" SEC_EQAPO

    ${If} $EqApoInstalled == "1"
        DetailPrint "EqualizerAPO already installed — skipping."
        Goto done
    ${EndIf}

    DetailPrint "Downloading EqualizerAPO..."
    inetc::get /CAPTION "Downloading EqualizerAPO..." \
               /BANNER  "Please wait while EqualizerAPO is downloaded." \
               "${EQAPO_URL}" "${EQAPO_TMP}" /END
    Pop $0
    ${If} $0 != "OK"
        MessageBox MB_ICONEXCLAMATION|MB_OK \
            "Failed to download EqualizerAPO ($0).$\r$\nPlease install it manually from:$\r$\nhttps://equalizerapo.sourceforge.io"
        Goto done
    ${EndIf}

    DetailPrint "Installing EqualizerAPO..."
    ExecWait '"${EQAPO_TMP}"' $0
    Delete "${EQAPO_TMP}"

    ${If} $0 != "0"
        MessageBox MB_ICONEXCLAMATION|MB_OK \
            "EqualizerAPO setup was cancelled or failed.$\r$\nG-EQ will still be installed, but EQ controls will be disabled until EqualizerAPO is present."
    ${Else}
        SetRebootFlag true
    ${EndIf}

    done:

SectionEnd

Section "G-EQ" SEC_APP

    SetOutPath "$INSTDIR"
    File /r /x "*.pdb" "app\"

    ; Desktop shortcut
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

    ; Start menu
    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"  "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk"    "$INSTDIR\Uninstall.exe"

    ; Add/Remove Programs
    WriteRegStr   HKLM "${REG_UNINSTALL}" "DisplayName"      "${APP_NAME}"
    WriteRegStr   HKLM "${REG_UNINSTALL}" "DisplayVersion"   "${APP_VERSION}"
    WriteRegStr   HKLM "${REG_UNINSTALL}" "Publisher"        "${PUBLISHER}"
    WriteRegStr   HKLM "${REG_UNINSTALL}" "InstallLocation"  "$INSTDIR"
    WriteRegStr   HKLM "${REG_UNINSTALL}" "UninstallString"  "$INSTDIR\Uninstall.exe"
    WriteRegStr   HKLM "${REG_UNINSTALL}" "DisplayIcon"      "$INSTDIR\${APP_EXE}"
    WriteRegDWORD HKLM "${REG_UNINSTALL}" "NoModify"         1
    WriteRegDWORD HKLM "${REG_UNINSTALL}" "NoRepair"         1
    WriteRegDWORD HKLM "${REG_UNINSTALL}" "EstimatedSize"    105000

    WriteUninstaller "$INSTDIR\Uninstall.exe"

SectionEnd

; ── Finish helpers ────────────────────────────────────────────────────────────

Function RequestReboot
    Reboot
FunctionEnd

; ── Uninstall ─────────────────────────────────────────────────────────────────

Section "Uninstall"

    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "GamingEqualizer"

    RMDir /r "$INSTDIR"

    Delete "$DESKTOP\${APP_NAME}.lnk"
    RMDir /r "$SMPROGRAMS\${APP_NAME}"

    DeleteRegKey HKLM "${REG_UNINSTALL}"

SectionEnd
