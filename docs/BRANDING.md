# AZOTH identity and assets

The product name is **AZOTH — by Veilric**, maintained under **The Alchemical Order**. The intended public source repository is Veilric/AZOTH-pub-release. Repository naming does not change the application's installation identity or settings paths.

Official canonical artwork lives in [src/AZOTH.App/Assets/Icons](../src/AZOTH.App/Assets/Icons):

- `azoth.svg` / `azoth.png`: transparent gold square symbol used by the application, launch stage, tray, and package surfaces.
- `azoth.ico`: multi-size Windows application and Setup icon.
- `azoth-horizontal-dark.svg` / `.png`: official wordmark on its supplied dark background, used on About in both themes.
- `azoth-dark.svg` and `azoth-horizontal.svg`, with PNG equivalents: supplied official alternatives retained alongside the selected assets.

Preserve artwork proportions, transparency, and intended contrast. Use the official symbol or wordmark when documenting the current product. Historical Filee visuals and embedded font identities under test fixtures remain to exercise upstream behavior; they are not current AZOTH branding.

The launch presentation uses a centered official symbol on a clean theme-colored background for 1.4 seconds before the main interface appears. Reduced motion skips it. Tray/single-instance reopening does not replay it, and hiding/minimizing finishes the stage. Preserve these accessibility and lifecycle behaviors when changing launch visuals.

AZOTH derives from Filee by **Myeonggu Kim (KnifeLemon) and contributors**. Keep the About credits, original [MIT license](../LICENSE), [NOTICE.md](../NOTICE.md), and [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md). Do not replace upstream copyright with AZOTH attribution. Dependencies and optional engines retain their own notices and licenses.

Changes to branding must also preserve installer paths, publisher, uninstall identity, and existing user data unless an explicit migration is designed and verified. A new public repository or logo is not a reason to silently move an existing installation.
