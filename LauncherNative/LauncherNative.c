/*
	Copyright (C) 2016-2018 Hajin Jang
	Licensed under MIT License.

	MIT License

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in all
	copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
	SOFTWARE.
*/

#include "targetver.h"

#define WIN32_LEAN_AND_MEAN
// Windows SDK Headers
#include <windows.h>
#include <strsafe.h>
#include <shlwapi.h>
#include <shellapi.h>

// C Runtime Headers
#include <stdio.h>
#include <stdlib.h>
#include <stdbool.h>

// Resource Headers
#include "resource.h"

// Constants
#define MAX_PATH_LONG	32768
#define MAX_MSG_BUF		256

#define DOTNETFX_INSTALLER_URL			L"https://www.microsoft.com/en-us/download/details.aspx?id=56116"

#define ERR_MSG_UNABLE_TO_GET_ABSPATH	L"Unable to query absolute path of PEBakeryLauncher.exe"
#define ERR_MSG_UNABLE_TO_FIND_BINARY	L"Unable to find PEBakery."
#define ERR_MSG_UNABLE_TO_LAUNCH_BINARY	L"Unable to launch PEBakery."
#define ERR_MSG_INSTALL_DOTNETFX_471	L"PEBakery requires .Net Framework 4.7.1 or newer."
#define ERR_CAP_INSTALL_DOTNETFX_471	L"Install .Net Framework 4.7.1"

// Prototypes
bool CheckNetFrameworkVersion();
WCHAR* GetParameters();
void PrintError(WCHAR* errMsg);
void PrintErrorAndOpenUrl(WCHAR* errMsg, WCHAR* errCaption, WCHAR* url);

// These buffers are too large to go in local stack
WCHAR AbsPath[MAX_PATH_LONG] = { 0 };
WCHAR PEBakeryPath[MAX_PATH_LONG] = { 0 };

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                     _In_opt_ HINSTANCE hPrevInstance,
                     _In_ LPWSTR    lpCmdLine,
                     _In_ int       nCmdShow)
{
    UNREFERENCED_PARAMETER(hPrevInstance);
    UNREFERENCED_PARAMETER(lpCmdLine);
	int hRes = 0;

	// Get absolute path of PEBakery.exe in absPath
	const DWORD absPathLen = GetModuleFileNameW(NULL, AbsPath, MAX_PATH_LONG);
	if (absPathLen == 0)
		PrintError(ERR_MSG_UNABLE_TO_GET_ABSPATH);
		
	AbsPath[MAX_PATH_LONG - 1] = '\0'; // NULL guard for Windows XP
	const PWSTR posDir = StrRChrW(AbsPath, NULL, L'\\');
	if (posDir == NULL)
		exit(1);
	posDir[0] = '\0';
	StringCchCopyW(PEBakeryPath, MAX_PATH_LONG, AbsPath);
	StringCchCatW(PEBakeryPath, MAX_PATH_LONG, L"\\Binary\\PEBakery.exe");

	// Check if PEBakery.exe exists
	if (!PathFileExistsW(PEBakeryPath))
		PrintError(ERR_MSG_UNABLE_TO_FIND_BINARY);

	// Check if .Net Framework 4.7.1 or newer is installed
	if (!CheckNetFrameworkVersion())
		PrintErrorAndOpenUrl(ERR_MSG_INSTALL_DOTNETFX_471, ERR_CAP_INSTALL_DOTNETFX_471, DOTNETFX_INSTALLER_URL);

	// According to MSDN, ShellExecute's return value can be casted only to int.
	// In mingw, size_t casting should be used to evade [-Wpointer-to-int-cast] warning.
	hRes = (int)ShellExecuteW(NULL, NULL, PEBakeryPath, GetParameters(), AbsPath, SW_SHOWNORMAL);
	if (hRes <= 32)
		PrintError(ERR_MSG_UNABLE_TO_LAUNCH_BINARY);

	return 0;
}

bool CheckNetFrameworkVersion()
{ // https://docs.microsoft.com/ko-kr/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed#net_b
	HKEY hKey = INVALID_HANDLE_VALUE;
	const WCHAR* ndpPath = L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";
	const WCHAR* ndpValue = L"Release";
	DWORD revision = 0;
	DWORD dwordSize = sizeof(DWORD);
	bool ret = false;

	if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, ndpPath, 0, KEY_READ | KEY_WOW64_64KEY, &hKey) != ERROR_SUCCESS)
		return false;

	if (RegQueryValueExW(hKey, ndpValue, NULL, NULL, (LPBYTE)&revision, &dwordSize) != ERROR_SUCCESS)
		goto out;

	// PEBakery requires .Net Framework 4.7.1 or later
	if (461308 <= revision)
		ret = true;

out:
	RegCloseKey(hKey);
	return ret;
}

// Get start point of argv[1] from command line
WCHAR* GetParameters()
{
	WCHAR* cmdRawLine = GetCommandLineW();
	WCHAR* cmdParam = NULL;

	// Case 1 : Simplest form of 'single param', no space
	// Ex) calc.exe
	if (StrChrW(cmdRawLine, L' ') == NULL)
		cmdParam = NULL;
	else // It is 'multiple params' OR 'single param with quotes'
	{
		if (StrChrW(cmdRawLine, L'\"') == NULL)
			// Case 2 : 'multiple params' without quotes
			// Ex) notepad.exe Notepad-UTF8.txt
			cmdParam = StrChrW(cmdRawLine, L' ');
		else
		{
			// Detect if first parameter has quotes
			if (StrChrW(cmdRawLine, L'\"') == cmdRawLine)
			{
				wchar_t* cmdLeftQuote = NULL; // Start of first parameter
				wchar_t* cmdRightQuote = NULL; // End of first parameter
				cmdLeftQuote = StrChrW(cmdRawLine, L'\"');
				cmdRightQuote = StrChrW(cmdLeftQuote + 1, L'\"');

				// Case 3 : Single param with quotes on first param
				// Ex) "Simple Browser.exe"
				if (StrChrW(cmdRightQuote + 1, L' ') == NULL)
					cmdParam = NULL;
				// Case 4 : Multiple param with quotes on first param
				// Ex) "Simple Browser.exe" joveler.kr
				else
					cmdParam = StrChrW(cmdRightQuote + 1, L' '); // Spaces between cmdLeftQuote and cmdRightQuote must be ignored
			}
			// Case 5 : Multiple param, but no quotes on first param
			// Ex) notepad.exe "Notepad UTF8.txt"
			else
				cmdParam = StrChrW(cmdRawLine, L' ');
		}
	}

	return cmdParam;
}

void PrintError(WCHAR* errMsg)
{
	fwprintf(stderr, L"%ws\n", errMsg);
	MessageBoxW(NULL, errMsg, L"Error", MB_OK | MB_ICONERROR);
	exit(1);
}

void PrintErrorAndOpenUrl(WCHAR* errMsg, WCHAR* errCaption, WCHAR* url)
{
	fwprintf(stderr, L"%ws\n", errMsg);
	MessageBoxW(NULL, errMsg, errCaption, MB_OK | MB_ICONERROR);
	ShellExecuteW(NULL, NULL, url, NULL, NULL, SW_SHOWNORMAL);
	exit(1);
}