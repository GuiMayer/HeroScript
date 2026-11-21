# Audio packs for the showcase

The preferred interface and elemental sounds are from Willosuna:

- [Essential UI SFX](https://willosuna.itch.io/essential-ui-sfx), release r5.
- [Magic Spells SFX Lite](https://willosuna.itch.io/magic-spells-sfx-lite), release v2.1/r2.

Both allow use in commercial and personal games but prohibit redistribution as a standalone sound pack or collection. The optional local files are ignored by Git. Keep the original ZIPs and licenses. To install after cloning, download the free versions from the author and run:

```powershell
./tools/install-audio-packs.ps1 -UiZip 'C:/path/essential-ui-sfx-r5.zip' -MagicZip 'C:/path/magic-spells-sfx-lite-v21-rate-r2.zip'
```

Open/import the project with Godot. The manifest selects the preferred clips when installed and uses the existing permissively licensed Kenney clips otherwise. Card hover intentionally remains the original three Kenney variants with unchanged gain, cooldown and polyphony.

Elemental impacts follow the published calculation tags associated with each application, never an inferred card name. Known status IDs cover elemental status applications and residual ticks without tags. All feedback is cosmetic; the audio pool, user volume settings, cooldowns, silence on zero damage and deterministic gameplay are preserved.
