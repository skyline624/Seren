/**
 * Git hooks pour Seren. Voir CLAUDE.md : `dotnet format` + `pnpm lint` + `pnpm typecheck`
 * doivent passer avant chaque commit.
 *
 * Installer : `npm install` à la racine, ou `npx simple-git-hooks` pour forcer.
 */
module.exports = {
  'pre-commit': [
    'dotnet format src/server/Seren.sln --verify-no-changes',
    'pnpm -C src/ui lint',
    'pnpm -C src/ui typecheck',
  ].join(' && '),
  'commit-msg': 'node tools/scripts/check-conventional-commit.js $1',
}
