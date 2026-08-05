#!/usr/bin/env python3
"""
Exports ArmedConflict's data/ layer from Kotlin to JSON, for import as Unity ScriptableObjects.

WHY A PARSER AND NOT A HAND PORT: the Android build remains the shipping build, so its data
keeps moving. Transcribing 29 levels once would be wrong the first time someone tweaks a level
anchor. This is re-runnable.

WHY NOT RUN THE KOTLIN: exporting from the app would mean adding a serializer to the shipping
repo, which CLAUDE.md now forbids ("do not begin porting work HERE").

Scope: it parses the narrow, highly-regular subset of Kotlin these definition files actually
use — named constructor arguments, nested calls, listOf/setOf/emptyList, numeric/string/bool
literals, enum and object references. It is NOT a Kotlin parser and will refuse loudly rather
than guess.

Usage:  python3 tools/export_kotlin_data.py <path-to-ArmedConflict> [-o data.json]
"""

import argparse
import json
import re
import sys
from pathlib import Path

# ---------------------------------------------------------------- tokenizer / value parser

NUM = re.compile(r'^-?\d+(\.\d+)?([fF])?$')


class Cursor:
    """Character cursor over one source file, aware of strings and comments."""

    def __init__(self, text):
        self.s = text
        self.i = 0

    def skip_ws(self):
        while self.i < len(self.s):
            c = self.s[self.i]
            if c in ' \t\r\n':
                self.i += 1
            elif self.s.startswith('//', self.i):
                nl = self.s.find('\n', self.i)
                self.i = len(self.s) if nl < 0 else nl + 1
            elif self.s.startswith('/*', self.i):
                end = self.s.find('*/', self.i)
                self.i = len(self.s) if end < 0 else end + 2
            else:
                return

    def peek(self):
        self.skip_ws()
        return self.s[self.i] if self.i < len(self.s) else ''

    def take(self, n=1):
        self.skip_ws()
        out = self.s[self.i:self.i + n]
        self.i += n
        return out

    def expect(self, ch):
        got = self.take(len(ch))
        if got != ch:
            raise SyntaxError(f'expected {ch!r} got {got!r} at offset {self.i}')

    def read_string(self):
        self.skip_ws()
        assert self.s[self.i] == '"'
        self.i += 1
        out = []
        while self.i < len(self.s):
            c = self.s[self.i]
            if c == '\\':
                out.append(self.s[self.i + 1])
                self.i += 2
            elif c == '"':
                self.i += 1
                return ''.join(out)
            else:
                out.append(c)
                self.i += 1
        raise SyntaxError('unterminated string')

    def read_ident(self):
        self.skip_ws()
        m = re.match(r'[A-Za-z_][A-Za-z0-9_.]*', self.s[self.i:])
        if not m:
            raise SyntaxError(f'expected identifier at offset {self.i}: {self.s[self.i:self.i+40]!r}')
        self.i += m.end()
        return m.group(0)


def parse_value(cur):
    """Parses one Kotlin expression: literal, listOf(...), Ctor(...), reference, or negation."""
    c = cur.peek()
    if c == '"':
        return cur.read_string()
    if c == '(':                       # parenthesised arithmetic — evaluate numerically
        return parse_arith(cur)

    # Numeric literal. MUST come before read_ident, which cannot start with a digit — routing
    # numbers through it is why every `maxHp = 32` silently failed to parse on the first pass.
    # Hex FIRST: 0xFF4A90D9 otherwise matches the decimal rule as plain `0` and leaves
    # `xFF4A90D9` behind as a bogus identifier — which is how every background colour came
    # out black on the first export.
    hm = re.match(r'0[xX][0-9a-fA-F]+[uUlL]*', cur.s[cur.i:])
    if hm:
        cur.i += hm.end()
        return int(hm.group(0).rstrip('uUlL'), 16)

    m = re.match(r'[-+]?\d+(\.\d+)?([eE][-+]?\d+)?[fFdDLl]?', cur.s[cur.i:])
    if m and (c.isdigit() or c in '-+'):
        cur.i += m.end()
        raw = m.group(0)
        body = raw.rstrip('fFdDLl').lstrip('+')
        return float(body) if ('.' in body or 'e' in body or 'E' in body
                               or raw[-1] in 'fFdD') else int(body)

    ident = cur.read_ident()

    if ident in ('listOf', 'setOf', 'listOfNotNull'):
        return parse_call_args(cur, positional_only=True)
    if ident in ('emptyList', 'emptySet'):
        cur.expect('(')
        cur.expect(')')
        return []
    if ident == 'true':
        return True
    if ident == 'false':
        return False
    if ident == 'null':
        return None
    if NUM.match(ident):
        return float(ident.rstrip('fF')) if ('.' in ident or ident.endswith(('f', 'F'))) else int(ident)

    if ident.endswith('.let') and cur.peek() == '{':
        # read_ident deliberately swallows dotted names so UnitDefinitions.Rifleman stays one
        # reference — which also swallows the ".let". Peel it back off here.
        recv = {'__ref': ident[:-4]}
        cur.take()                       # {
        param = cur.read_ident()
        cur.expect('->')
        body = parse_chain(cur, parse_value(cur))
        cur.expect('}')
        return {'__method': 'let', '__on': recv, '__param': param, '__body': body}

    if cur.peek() == '(':              # constructor / factory call
        args = parse_call_args(cur)
        return {'__ctor': ident, **({'__args': args} if isinstance(args, list) else args)}

    return {'__ref': ident}            # UnitDefinitions.Rifleman, ProjectileType.Shell, ...


def parse_chain(cur, val):
    """
    Trailing member chains: .copy(...), .scaled(...), ?.copy(...), and the one lambda form these
    files use — X.let { name -> body }.

    The lambda matters: Level6 and L29 give the tank a level-local cannon override via
    `PlayerTank.let { tank -> tank.copy(cannon = tank.cannon?.copy(ammoPerBattle = 6)) }`.
    Dropping it would silently hand L6 three shells instead of six against 637 HP of masonry —
    a balance change disguised as a parse failure.
    """
    while True:
        c = cur.peek()
        if c == '?' and cur.s[cur.i + 1] == '.':
            cur.take(2)
            safe = True
        elif c == '.':
            cur.take()
            safe = False
        else:
            return val
        meth = cur.read_ident()
        if meth == 'let' and cur.peek() == '{':
            cur.take()                       # {
            param = cur.read_ident()
            cur.expect('->')
            body = parse_chain(cur, parse_value(cur))
            cur.expect('}')
            val = {'__method': 'let', '__on': val, '__param': param, '__body': body}
        else:
            margs = parse_call_args(cur) if cur.peek() == '(' else {}
            val = {'__method': meth, '__on': val, '__args': margs, '__safe': safe}
    return val


def parse_arith(cur):
    """Numeric-only arithmetic, enough for things like (0.5f * 1.2f)."""
    cur.expect('(')
    depth, start = 1, cur.i
    while depth:
        ch = cur.s[cur.i]
        depth += (ch == '(') - (ch == ')')
        cur.i += 1
    expr = cur.s[start:cur.i - 1]
    cleaned = re.sub(r'([0-9.])[fF]\b', r'\1', expr)
    if not re.fullmatch(r'[-+*/(). 0-9]+', cleaned):
        raise SyntaxError(f'non-numeric parenthesised expression: {expr!r}')
    return eval(cleaned)               # numeric literals only, guarded above


def parse_call_args(cur, positional_only=False):
    """Returns dict of named args, or list when positional."""
    cur.expect('(')
    named, positional = {}, []
    while True:
        if cur.peek() == ')':
            cur.take()
            break
        save = cur.i
        name = None
        if not positional_only:
            try:
                ident = cur.read_ident()
                if cur.peek() == '=' and cur.s[cur.i + 1] != '=':
                    cur.take()
                    name = ident
                else:
                    cur.i = save
            except SyntaxError:
                cur.i = save
        val = parse_value(cur)
        val = parse_chain(cur, val)
        (positional.append(val) if name is None else named.__setitem__(name, val))
        if cur.peek() == ',':
            cur.take()
    if positional_only:
        return positional
    if positional and not named:
        return positional
    if positional:
        named['__positional'] = positional
    return named


# ---------------------------------------------------------------- top-level extraction

# Member calls that DERIVE one definition from another. A val whose right-hand side is one of
# these over an existing definition is itself a definition and must be exported.
DERIVING_METHODS = ('copy', 'scaled')


def extract_vals(path, want_ctors, failures):
    """
    Finds `val Name = Ctor(...)` at any indent, for the constructors we care about.

    A declaration that LOOKS like one of our constructors but fails to parse is recorded in
    `failures`, never silently skipped — a silently dropped level is indistinguishable from a
    level that does not exist, which is exactly the kind of quiet data loss this port cannot
    afford.
    """
    text = path.read_text()
    out = {}
    for m in re.finditer(r'\bval\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?=[A-Za-z_])', text):
        name = m.group(1)
        looks_wanted = any(text[m.end():].startswith(c) for c in want_ctors)
        cur = Cursor(text)
        cur.i = m.end()
        try:
            v = parse_chain(cur, parse_value(cur))
        except SyntaxError as e:
            if looks_wanted:
                failures.append((path.name, name, str(e)[:120]))
            continue
        # `val EnemyRifleman = Rifleman.copy(...)` parses as a ctor literally named
        # "Rifleman.copy", because read_ident swallows dots. Without this clause the four
        # Enemy* variants vanish silently and every enemy group in every level loses its unit.
        rc = root_ctor(v)
        # `val EnemyRifleman = Rifleman.copy(...)` parses as a ctor literally NAMED
        # "Rifleman.copy", because read_ident swallows dots — and `val FortressTier =
        # FortressTierUnscaled.scaled()` lands in exactly the same shape with the other method
        # name. Only .copy was accepted, so FortressTier was dropped; and since a bare identifier
        # does not start with a wanted ctor name, `looks_wanted` was false too, so it was not even
        # reported as unparsed. Five levels placed a structure that did not exist and threw on
        # load. Any DERIVING method belongs here, not just copy.
        derived = isinstance(rc, str) and rc.rpartition('.')[2] in DERIVING_METHODS
        if isinstance(v, dict) and (rc in want_ctors or derived
                                    or any(has_method(v, mth) for mth in DERIVING_METHODS)):
            out[name] = v
    return out


def root_ctor(v):
    """Walks a trailing chain (.scaled().copy()) back to the constructor underneath it."""
    seen = 0
    while isinstance(v, dict) and '__on' in v and seen < 12:
        v = v['__on']
        seen += 1
    return v.get('__ctor') if isinstance(v, dict) else None


def has_method(v, name):
    seen = 0
    while isinstance(v, dict) and seen < 12:
        if v.get('__method') == name:
            return True
        v = v.get('__on')
        seen += 1
    return False


def collect_list_val(path, val_name):
    """Parses `val all = listOf(...)` style declarations."""
    text = path.read_text()
    m = re.search(r'\bval\s+' + re.escape(val_name) + r'\s*(?::[^=]+)?=\s*(?=[A-Za-z_])', text)
    if not m:
        return None
    cur = Cursor(text)
    cur.i = m.end()
    return parse_value(cur)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('repo')
    ap.add_argument('-o', '--out', default='data.json')
    args = ap.parse_args()

    d = Path(args.repo) / 'app/src/main/java/com/dullesengineering/armedconflict/data'
    if not d.is_dir():
        sys.exit(f'not found: {d}')

    failures = []
    payload = {
        'units':       extract_vals(d / 'UnitDefinition.kt', {'UnitDefinition'}, failures),
        'structures':  extract_vals(d / 'StructureDefinition.kt', {'StructureDefinition'}, failures),
        'backgrounds': extract_vals(d / 'BackgroundDefinition.kt', {'BackgroundDefinition'}, failures),
        'levels':      extract_vals(d / 'LevelDefinition.kt', {'LevelDefinition'}, failures),
        'stages':      extract_vals(d / 'StageDefinition.kt', {'StageDefinition'}, failures),
        'levelOrder':  collect_list_val(d / 'LevelDefinition.kt', 'all'),
        'stageOrder':  collect_list_val(d / 'StageDefinition.kt', 'all'),
    }

    Path(args.out).write_text(json.dumps(payload, indent=1))
    for k in ('units', 'structures', 'backgrounds', 'levels', 'stages'):
        print(f'{k:12} {len(payload[k]):3d}')
    order = payload['levelOrder']
    print(f'levelOrder   {len(order) if isinstance(order, list) else "?"}')

    payload['unparsed'] = [{'file': f, 'name': n, 'error': e} for f, n, e in failures]
    Path(args.out).write_text(json.dumps(payload, indent=1))
    if failures:
        print(f'\nUNPARSED ({len(failures)}) — these need hand attention, they are NOT in the output:')
        for f, n, e in failures:
            print(f'  {f}:{n}\n    {e}')


if __name__ == '__main__':
    main()
