# Third-Party Notices

QS Launcher is a native C# / WPF application.

## .NET Runtime

The self-contained portable build includes the Microsoft .NET Runtime and Windows Desktop
Runtime, distributed under the MIT License.
Copyright (c) .NET Foundation and Contributors.

https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

## Services used at runtime (not bundled)

- Mojang / Microsoft Minecraft version manifest, assets, libraries and Java runtime index.
  Minecraft is a trademark of Mojang Synergies AB. QS Launcher is not affiliated with Mojang
  or Microsoft.
- Modrinth API for mod and modpack search/download. Mod content belongs to its respective authors.
- Fabric, Quilt, Forge and NeoForge metadata and installers, downloaded from their official
  endpoints at the user's request.
- authlib-injector for third-party Yggdrasil authentication, downloaded and SHA-256 verified at
  the user's request. https://github.com/yushijinhun/authlib-injector

QS Launcher does not bundle PCL2 code, assets, icons, layouts, palettes or animation curves.
