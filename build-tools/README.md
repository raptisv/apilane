# Build tools

## Tailwind CSS CLI

The Portal's CSS (`src/Apilane.Portal/wwwroot/assets/vendor/tailwind/tailwind.css`) is compiled from
`src/Apilane.Portal/tailwind/input.css` using Tailwind's **standalone CLI** — a single native
executable, no Node/npm required. The binary is platform-specific and not committed to the repo
(~100MB+); download it once per machine:

```bash
# Windows
curl -sL "https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-windows-x64.exe" -o build-tools/tailwindcss.exe

# macOS (Apple Silicon)
curl -sL "https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-macos-arm64" -o build-tools/tailwindcss && chmod +x build-tools/tailwindcss

# Linux
curl -sL "https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64" -o build-tools/tailwindcss && chmod +x build-tools/tailwindcss
```

### Rebuilding the CSS

Whenever you change class names in a `.cshtml` view, or edit `src/Apilane.Portal/tailwind/input.css`
itself (the black/blue theme tokens and the Bootstrap-class-name component shim live there), rebuild
from the repo root:

```bash
build-tools/tailwindcss.exe -i src/Apilane.Portal/tailwind/input.css -o src/Apilane.Portal/wwwroot/assets/vendor/tailwind/tailwind.css --minify
```

The compiled output **is** committed (it's the actual served asset, same as the other vendored
libraries under `wwwroot/assets/vendor/`) — only the CLI binary itself is gitignored.
