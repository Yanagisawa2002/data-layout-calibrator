"""Complete persisted-output correctness; no workloads, clocks or benchmark timing."""
import argparse
import array
import hashlib
import json
import math
from pathlib import Path
import struct


def sha(path):
    with Path(path).open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()


def babel(prefix):
    prefix = str(prefix)
    data = json.loads(Path(prefix + '.json').read_text(encoding='utf-8-sig'))
    n, iterations = data['count'], data['iterations']
    assert (n, iterations) == (33554432, 100), 'Default workload changed'
    a, b, c = 0.1, 0.2, 0.0
    for _ in range(iterations):
        c = a; b = .4 * c; c = a + b; a = b + .4 * c
    expected_sum = a * b * n
    limit = 2.220446049250313e-16
    assert math.isfinite(data['sum']) and abs(data['sum'] - expected_sum) <= max(abs(data['sum']), abs(expected_sum)) * limit * 10000000
    digest = hashlib.sha256()
    mismatches, max_relative = 0, 0.0
    with Path(prefix + '.bin').open('rb') as f:
        for expected in (a, b, c):
            for offset in range(0, n, 65536):
                size = min(65536, n - offset)
                block = f.read(size * 8)
                assert len(block) == size * 8, 'Truncated full output'
                digest.update(block)
                if block == struct.pack('<d', expected) * size:
                    continue
                values = array.array('d'); values.frombytes(block)
                for actual in values:
                    rel = abs(actual - expected) / max(abs(actual), abs(expected))
                    max_relative = max(max_relative, rel)
                    if not math.isfinite(actual) or rel > limit * 100: mismatches += 1
        assert not f.read(1), 'Trailing output'
    assert mismatches == 0, f'{mismatches} incorrect array values'
    return dict(passed=True, workload='BabelStream variant', checkedArrayValues=3*n, checkedDot=True,
                maxArrayRelativeError=max_relative, dotRelativeError=abs(data['sum']-expected_sum)/max(abs(data['sum']),abs(expected_sum)),
                outputSha256=digest.hexdigest(), timingSha256=sha(prefix+'.json'))


def llama(reference, candidate, input_path):
    paths = [str(reference)+'.bin', str(candidate)+'.bin', str(input_path)]
    values = []
    for p in paths:
        assert Path(p).stat().st_size == 65536*28
        v = array.array('f'); v.frombytes(Path(p).read_bytes()); values.append(v)
    reference_values, candidate_values, inputs = values
    max_abs, max_rel, exact = 0., 0., True
    for i, (a, b) in enumerate(zip(reference_values, candidate_values)):
        assert math.isfinite(a) and math.isfinite(b), f'Nonfinite at field {i}'
        error = abs(a-b); max_abs = max(max_abs,error)
        max_rel = max(max_rel,error/max(1e-30,abs(a),abs(b)))
        exact = exact and struct.pack('<f',a)==struct.pack('<f',b)
        # Predeclared local criterion; upstream code_comp supplies no checker.
        assert error <= 1e-6 + 2e-5*max(abs(a),abs(b)), f'Parity failed at field {i}: {a} vs {b}'
        if i%7 == 6:
            assert struct.pack('<f',a)==struct.pack('<f',inputs[i])==struct.pack('<f',b), f'Mass changed at {i}'
    return dict(passed=True, workload='LLAMA code_comp auxiliary example', criterion='local abs 1e-6 + symmetric rel 2e-5; masses bit exact',
                checkedFields=len(reference_values), maxAbsoluteError=max_abs, maxRelativeError=max_rel, bitExact=exact,
                referenceSha256=sha(paths[0]), outputSha256=sha(paths[1]), inputSha256=sha(paths[2]))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--babel', type=Path)
    parser.add_argument('--llama', nargs=3, metavar=('REFERENCE_PREFIX','CANDIDATE_PREFIX','INPUT'))
    parser.add_argument('--receipt', required=True, type=Path)
    args = parser.parse_args()
    if bool(args.babel) == bool(args.llama): parser.error('Select exactly one workload')
    assert not args.receipt.exists(), 'Receipt already exists'
    try:
        result = babel(args.babel) if args.babel else llama(*args.llama)
    except Exception as error:
        args.receipt.write_text(json.dumps(dict(passed=False,error=str(error)),indent=2))
        raise
    args.receipt.write_text(json.dumps(result,indent=2))
    print(json.dumps(result))
