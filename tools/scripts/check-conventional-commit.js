#!/usr/bin/env node
/**
 * commit-msg hook : vérifie que le message suit les conventional commits.
 * Format accepté : type(scope)?: subject
 * Types : feat, fix, refactor, docs, test, chore, build, ci, perf, style, revert
 *
 * Usage : node tools/scripts/check-conventional-commit.js .git/COMMIT_EDITMSG
 */
const { readFileSync } = require('node:fs')

const TYPES = [
  'feat', 'fix', 'refactor', 'docs', 'test',
  'chore', 'build', 'ci', 'perf', 'style', 'revert',
]

const messagePath = process.argv[2]
if (!messagePath) {
  process.stderr.write('check-conventional-commit: missing message file path\n')
  process.exit(1)
}

const raw = readFileSync(messagePath, 'utf8')
const firstLine = raw
  .split('\n')
  .find((line) => !line.startsWith('#') && line.trim().length > 0)

if (!firstLine) {
  process.stderr.write('check-conventional-commit: empty commit message\n')
  process.exit(1)
}

const pattern = new RegExp(`^(${TYPES.join('|')})(\\([\\w.-]+\\))?!?: .{1,}$`)
if (!pattern.test(firstLine)) {
  process.stderr.write(
    `check-conventional-commit: "${firstLine}" does not match conventional commit format.\n`
    + `Expected: <type>(<scope>)?: <subject>\n`
    + `Allowed types: ${TYPES.join(', ')}\n`,
  )
  process.exit(1)
}

process.exit(0)
