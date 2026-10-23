# Third-party notices

The Hyundai USA wire protocol, endpoint paths, header constants, payload shapes and field mappings in `Core/HyundaiUsClient.cs` were adapted into C# from:

- Hyundai-Kia-Connect / hyundai_kia_connect_api
- `hyundai_kia_connect_api/HyundaiBlueLinkApiUSA.py` and `const.py`
- Repository: https://github.com/Hyundai-Kia-Connect/hyundai_kia_connect_api
- Reference revision: `6b966c021bd3e2e7fd39c979b2abb3defeab8a2b`
- Source: https://github.com/Hyundai-Kia-Connect/hyundai_kia_connect_api/blob/6b966c021bd3e2e7fd39c979b2abb3defeab8a2b/hyundai_kia_connect_api/HyundaiBlueLinkApiUSA.py
- Retrieved October 22, 2026 (US Eastern).
- MIT License, Copyright (c) 2021, Fuat Akgun. Full notice: `Licenses/HyundaiKiaConnect-MIT.txt`.

This application does not import, bundle or execute Python. The static client ID and client header constant are public interoperability values from that source, not an owner's credentials. Hyundai may change or revoke them.

The adapter is unofficial. It uses the normal HTTPS certificate verification provided by .NET and refuses redirects. Unlike the reference Python transport, it does not lower TLS security settings. Some platform/server TLS combinations may consequently fail and require investigation.

DejaVu Sans fonts: see `Assets/FONT_LICENSE.txt`. Pixel vehicle artwork: see `ART_NOTES.md`.
