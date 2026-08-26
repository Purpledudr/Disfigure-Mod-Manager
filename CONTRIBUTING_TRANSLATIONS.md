# Contributing translations

Community translations are reviewed through GitHub pull requests before they are added to the mod.

1. Open [community-translations](community-translations) and choose your language by its full name.
2. Use `Current-Translations` to improve the version currently shipped with the mod, or `Blank-Translations` to start clean.
3. Use `Additional-Blank-Translations` for the README and instructions, Mod Manager interface, or Sandbox Mode interface.
4. Fork this repository, then edit the file in your fork. GitHub's pencil button works; no software is required.
5. Change translation values only. Do not change, add, remove, or reorder source keys.
6. In blank files, leave `__SECTION_...`, `__COMMENT_...`, and number-only rows blank.
7. Do not add key bindings, numbers, rich-text tags, or selection arrows to blank files. Those are restored when the reviewed translation is prepared for the game.
8. Use natural human translation rather than unreviewed machine output.
9. Open a pull request titled `Translation: Full language name` and describe which sections are complete.

Incomplete work is welcome. Leave unfinished values blank so another contributor can continue safely.

Pull requests are checked automatically for valid JSON. Blank files are also checked for unchanged source keys, ordering, and protected rows. A maintainer reviews the wording before merging it into the main mod.
