NovaCore

NovaCore is a lightweight, customizable desktop shell for Windows 11, designed as an alternative to the default Windows Explorer shell.

The project provides the core components of a desktop environment, including a custom desktop, taskbar, Start menu, file manager, context menus, Recycle Bin integration, power controls, and fullscreen application detection. NovaCore can also be configured to run as a replacement for explorer.exe.

The main goal is to provide a simpler and more responsive Windows desktop experience while keeping the implementation modular and easy to extend.

Features
Custom desktop environment with persistent icon layouts
Lightweight taskbar with multi-monitor support
Start menu for launching and managing applications
Built-in file manager with basic file operations
Windows Recycle Bin integration
Desktop and application context menus
Automatic taskbar hiding for fullscreen applications
Shutdown, restart, sleep, and sign-out controls
Local configuration and persistent desktop settings
Optional Windows system and UI tweaks
Support for running NovaCore as the Windows shell
Design Goals

NovaCore is focused on simplicity, performance, and customization rather than reproducing the entire Windows Explorer experience.

The project aims to provide only the components required for a functional desktop environment while leaving room for users and developers to customize or extend the shell according to their needs.

Current Status

NovaCore is an experimental project and is still under active development. Some Windows shell functionality is not yet implemented, including system tray hosting, UWP application launching through AppUserModelID, multi-icon marquee selection, and dedicated vertical taskbar layouts.

Because NovaCore can replace the Windows Explorer shell, it should be considered experimental software. Testing it in a controlled environment is recommended.

Technology

NovaCore is written in C# using WPF and integrates with Windows through native Shell and system APIs.

The project is structured around several independent components responsible for desktop management, taskbar functionality, file operations, Windows Shell integration, fullscreen detection, power management, and configuration persistence.

Project Direction

The long-term goal of NovaCore is to evolve into a complete, lightweight, and extensible desktop shell for Windows 11.

Rather than modifying the existing Windows desktop, NovaCore explores the possibility of building a desktop environment from the ground up while maintaining compatibility with the Windows platform.
