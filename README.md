NovaCore

NovaCore is an ongoing project focused on developing a lightweight, customizable desktop environment for Windows 11.

The project is built around the idea of providing a modular alternative to the standard Windows desktop experience, with individual components developed independently and gradually integrated into a unified environment.

NovaCore is currently in an early stage of development. TinyShell is one of the first major components of the project, focusing on the Windows shell and providing the foundation for replacing and extending parts of the default explorer.exe experience.

Project Structure

NovaCore is not limited to a single application or shell. It is intended to serve as the foundation for a collection of components that together form a complete desktop environment.

TinyShell

TinyShell is the current shell-focused component of NovaCore.

It provides the basic functionality required for a custom Windows shell, including:

Custom desktop
Taskbar
Start menu
File manager
Desktop icons and persistent layouts
Context menus
Recycle Bin integration
Fullscreen application detection
Power management controls
Multi-monitor support
Persistent configuration
Optional Explorer shell replacement

TinyShell is currently the primary development focus and serves as a foundation for future NovaCore components.

Goals

NovaCore aims to provide a desktop environment that is:

Lightweight
Modular
Customizable
Responsive
Native to Windows
Easy to extend

Rather than modifying the existing Windows desktop indefinitely, the project explores what a Windows desktop could look like when its core components are implemented independently.

The goal is not to reproduce every feature of Windows Explorer, but to build a clean foundation that can be expanded as the project develops.

Development

NovaCore is an ongoing project and should be considered experimental software.

The architecture, APIs, components, and user interface may change significantly during development. Some functionality is incomplete, and current components should not be considered production-ready.

Future development may include additional desktop components, improved shell integration, application management, notification area support, improved window management, and further customization options.

Technology

The current implementation is primarily written in C# using WPF, with integration into Windows through native Win32 and Windows Shell APIs.

The project makes use of native Windows functionality where appropriate rather than attempting to reimplement operating-system functionality unnecessarily.

Current Status

NovaCore is not finished.

The current implementation represents an early stage of a larger project rather than a completed desktop environment. TinyShell currently provides the most developed part of the system, while other parts of the NovaCore architecture are still planned or under development.

Expect incomplete functionality, bugs, breaking changes, and architectural changes as development continues.

Disclaimer

NovaCore is provided "as is" and is intended primarily for experimentation, development, and testing.

The project is unfinished and may contain bugs or behave unexpectedly. Features may be incomplete, unstable, or removed or changed without notice.

Using NovaCore, particularly as a replacement for the Windows Explorer shell, is done entirely at your own risk.

The author provides no guarantee regarding stability, reliability, compatibility, data integrity, or suitability for any particular purpose and assumes no responsibility for data loss, system instability, software conflicts, configuration changes, or any other damage resulting from the use of the project.

Users should maintain appropriate backups and should test NovaCore in a controlled environment before using it as their primary desktop shell.

NovaCore is not affiliated with, endorsed by, or supported by Microsoft.
