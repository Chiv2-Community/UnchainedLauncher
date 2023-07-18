#include <vector>
#include <windows.h>
#include "Win32Helpers.h"

void ApplyShowCmd(const HWND hwnd, const std::vector<int> elementIds, const int state)
{
    for (auto i : elementIds)
    {
        ShowWindow(GetDlgItem(hwnd, i), state);
    }
}

