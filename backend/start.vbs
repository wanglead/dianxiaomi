Set WshShell = CreateObject("WScript.Shell")
WshShell.CurrentDirectory = "C:\Users\NIDHOGG\Documents\????\backend"
WshShell.Run "python run.py", 0, False
