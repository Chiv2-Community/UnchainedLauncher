#include <vector>
#include <windows.h>

void ApplyShowCmd(const HWND hwnd, const std::vector<int> elementIds, const int state)
{
    for (auto i : elementIds)
    {
        ShowWindow(GetDlgItem(hwnd, i), state);
    }
}