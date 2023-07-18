#include <windows.h>
#include <commctrl.h>
#include <vector>
#include <string>
#include <map>
#include "Win32Helpers.h"
#include "C2LauncherUtils.h"

#define ID_TAB_CONTROL 1

#define ID_INSTALLATION_TYPE_LABEL 2
#define ID_INSTALLATION_TYPE_DROPDOWN 3
#define ID_DETECT_BTN 4
const std::vector<int> SETTINGS_TAB_ELEMS = { 
    ID_INSTALLATION_TYPE_LABEL, 
    ID_INSTALLATION_TYPE_DROPDOWN, 
    ID_DETECT_BTN 
};

#define ID_MOD_BTN 7
#define ID_NO_MOD_BTN 8
const std::vector<int> LAUNCHER_TAB_ELEMS = { ID_MOD_BTN, ID_NO_MOD_BTN };

#define VERTICAL_TAB_MARGIN 40

#define NOT_SET_INSTALL_TEXT "Not Set"
#define STEAM_INSTALL_TEXT "Steam"
#define EGS_INSTALL_TEXT "Epic Games Store"


std::map<InstallationType, std::string> installationTypeTextMap = {
	{ InstallationType::NotSet, NOT_SET_INSTALL_TEXT },
	{ InstallationType::Steam, STEAM_INSTALL_TEXT },
	{ InstallationType::EpicGamesStore, EGS_INSTALL_TEXT }
};

bool RunAutoDetect(HWND hwnd)
{
    auto installationType = AutoDetectInstallationType();
    SendMessage(GetDlgItem(hwnd, ID_INSTALLATION_TYPE_DROPDOWN), CB_SETCURSEL, installationType, 0);


    if (installationType == InstallationType::NotSet) {
        MessageBox(hwnd, "Could not detect installation type. Please manually set it in the settings tab.", "Warning", MB_OK | MB_ICONWARNING);
        return false;
    }

    return true;
}

LRESULT CALLBACK WindowProcedure(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg)
    {
    case WM_CREATE:
    {
        // create the tab control
        HWND hwndTab = CreateWindowEx(0, WC_TABCONTROL, NULL, WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            10, 10, 320, 200, hwnd, (HMENU)ID_TAB_CONTROL, NULL, NULL);

        // Tab list element references
        TCITEM tie;
        tie.mask = TCIF_TEXT;

        // create the launcher tab
        tie.pszText = LPSTR("Launcher");
        TabCtrl_InsertItem(hwndTab, 0, &tie);

        
        // create the settings tab
        tie.pszText = LPSTR("Settings");
        TabCtrl_InsertItem(hwndTab, 1, &tie);

        // create the launcher controls
        HWND hwndModBtn = CreateWindowEx(0, "BUTTON", "Launch Modded", WS_CHILD | WS_VISIBLE,
            20, VERTICAL_TAB_MARGIN, 300, 30, hwnd, (HMENU)ID_MOD_BTN, NULL, NULL);

        HWND hwndNoModBtn = CreateWindowEx(0, "BUTTON", "Launch with No Mods", WS_CHILD | WS_VISIBLE,
            20, VERTICAL_TAB_MARGIN + 40, 300, 30, hwnd, (HMENU)ID_NO_MOD_BTN, NULL, NULL);

        // create the settings controls
        CreateWindowEx(0, "STATIC", "Installation Type:", WS_CHILD,
            20, VERTICAL_TAB_MARGIN, 300, 20, hwnd, (HMENU)ID_INSTALLATION_TYPE_LABEL, NULL, NULL);
        HWND hwndDropdown = CreateWindowEx(0, "COMBOBOX", NULL, WS_CHILD | CBS_DROPDOWN,
            20, VERTICAL_TAB_MARGIN + 20, 300, 200, hwnd, (HMENU)ID_INSTALLATION_TYPE_DROPDOWN, NULL, NULL);
        SendMessage(hwndDropdown, CB_ADDSTRING, 0, (LPARAM)NOT_SET_INSTALL_TEXT);
        SendMessage(hwndDropdown, CB_ADDSTRING, 0, (LPARAM)STEAM_INSTALL_TEXT);
        SendMessage(hwndDropdown, CB_ADDSTRING, 0, (LPARAM)EGS_INSTALL_TEXT);
        SendMessage(hwndDropdown, CB_SETCURSEL, 0, 0);

        HWND hwndDetectBtn = CreateWindowEx(0, "BUTTON", "Auto Detect", WS_CHILD,
            20, VERTICAL_TAB_MARGIN + 50, 300, 30, hwnd, (HMENU)ID_DETECT_BTN, NULL, NULL);

        break;
    }

    case WM_COMMAND:
    {
        LPSTR installTypeText = (LPSTR)""; // this is wrong

        switch (LOWORD(wp))
        {
        case ID_DETECT_BTN:
            RunAutoDetect(hwnd);
            break;
        case ID_MOD_BTN:
            // Handle the click event here
            break;
        case ID_NO_MOD_BTN:
            // Handle the click event here
            break;
        default:
            break;
        }

        break;
    }

    case WM_NOTIFY:
    {
        LPNMHDR nmhdr = (LPNMHDR)lp;
        if (nmhdr->idFrom == ID_TAB_CONTROL && nmhdr->code == TCN_SELCHANGE)
        {
            // show or hide controls based on the selected tab
            int iPage = TabCtrl_GetCurSel(nmhdr->hwndFrom);
            int showLauncher = iPage == 0 ? SW_SHOW : SW_HIDE;
            int showSettings = iPage == 1 ? SW_SHOW : SW_HIDE;

            ApplyShowCmd(hwnd, LAUNCHER_TAB_ELEMS, showLauncher);
            ApplyShowCmd(hwnd, SETTINGS_TAB_ELEMS, showSettings);
        }
        break;
    }

    case WM_DESTROY:
        PostQuitMessage(0);
        break;
    default:
        return DefWindowProc(hwnd, msg, wp, lp);
    }

    return 0;
}



int WINAPI WinMain(HINSTANCE hInst, HINSTANCE, LPSTR, int nCmdShow)
{
    INITCOMMONCONTROLSEX icex;
    icex.dwSize = sizeof(INITCOMMONCONTROLSEX);
    icex.dwICC = ICC_TAB_CLASSES;
    InitCommonControlsEx(&icex);

    WNDCLASS wc = { 0 };
    wc.lpfnWndProc = WindowProcedure;
    wc.hInstance = hInst;
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.lpszClassName = "win32app";
    wc.style = CS_HREDRAW | CS_VREDRAW;

    if (!RegisterClass(&wc))
        return -1;

    HWND hwnd = CreateWindowEx(0, "win32app", "Launcher", WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        CW_USEDEFAULT, CW_USEDEFAULT, 350, 250, 0, 0, hInst, 0);

    if (hwnd == NULL)
        return -1;

    ShowWindow(hwnd, nCmdShow);
    UpdateWindow(hwnd);

    MSG msg = { 0 };

    while (GetMessage(&msg, NULL, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    return 0;
}

