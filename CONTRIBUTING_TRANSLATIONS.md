# Contributing translations

Community translations are reviewed through GitHub pull requests before they are added to the mod.

1. Open [community-translations](community-translations) and choose your language file.
2. Fork this repository, then edit the file in your fork. GitHub's pencil button works; no software is required.
3. Translate only the blank value after each colon. Do not change, add, remove, or reorder keys.
4. Leave `__SECTION_...`, `__COMMENT_...`, and number-only rows blank.
5. Do not add key bindings, numbers, rich-text tags, or selection arrows. Those are restored when the reviewed translation is prepared for the game.
6. Use natural human translation rather than unreviewed machine output.
7. Open a pull request titled `Translation: Language name` and describe which sections are complete.

Incomplete work is welcome. Leave unfinished values blank so another contributor can continue safely.

Pull requests are checked automatically for valid JSON, unchanged source keys, and blank metadata rows. A maintainer reviews the wording before merging it into the main mod.
