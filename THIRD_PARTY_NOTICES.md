# Third-party notices

The Hyundai USA wire protocol, endpoint paths, header constants, payload shapes and field mappings in `Core/HyundaiUsClient.cs` were adapted into C# from:

- Hyundai-Kia-Connect / hyundai_kia_connect_api
- `hyundai_kia_connect_api/HyundaiBlueLinkApiUSA.py` and `const.py`
- Repository: https://github.com/Hyundai-Kia-Connect/hyundai_kia_connect_api
- Reference revision: `6b966c021bd3e2e7fd39c979b2abb3defeab8a2b`
- Source: https://github.com/Hyundai-Kia-Connect/hyundai_kia_connect_api/blob/6b966c021bd3e2e7fd39c979b2abb3defeab8a2b/hyundai_kia_connect_api/HyundaiBlueLinkApiUSA.py
- Retrieved October 22, 2026 (US Eastern).
- MIT License, Copyright (c) 2021, Fuat Akgun. Full notice: `Licenses/HyundaiKiaConnect-MIT.txt`.

The Kia Connect (US) wire protocol, endpoint paths, header constants, payload shapes and field mappings in `Core/KiaUsClient.cs` (and the read-only probe in `Tools/KiaProbe`) were adapted into C# from the same project:

- `hyundai_kia_connect_api/KiaUvoApiUSA.py` and `const.py`
- Reference revision: `bd93ccd38e8f582cbca03a786bb73a02159502ab`
- Source: https://github.com/Hyundai-Kia-Connect/hyundai_kia_connect_api/blob/bd93ccd38e8f582cbca03a786bb73a02159502ab/hyundai_kia_connect_api/KiaUvoApiUSA.py
- Retrieved November 8, 2026 (US Eastern). Same MIT license as above.

Unlike that source, the Kia adapter keeps .NET's default TLS settings (Kia accepted them on November 8, 2026), refuses redirects, and never resends a command after a session expires.

This application does not import, bundle or execute Python. The static client ID and client header constant are public interoperability values from that source, not an owner's credentials. Hyundai may change or revoke them.

The adapter is unofficial. It uses the normal HTTPS certificate verification provided by .NET and refuses redirects. Unlike the reference Python transport, it does not lower TLS security settings. Some platform/server TLS combinations may consequently fail and require investigation.

The outside temperature for the cold-weather range comes from the Open-Meteo forecast API (https://open-meteo.com/), whose data is licensed under CC BY 4.0 and credited in Settings → Range & units. Terms: https://open-meteo.com/en/terms.

DejaVu Sans fonts: see `Assets/FONT_LICENSE.txt`. Pixel vehicle artwork: see `ART_NOTES.md`.
