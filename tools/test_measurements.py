import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location("measurements", Path(__file__).with_name("run_measurements.py"))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class HarnessTests(unittest.TestCase):
    def test_recipe_independent(self):
        rows = list(m.recipe(74))
        self.assertEqual(74, len(rows))
        self.assertEqual(rows[35], rows[36])
        self.assertEqual(rows[72], rows[73])
        unique = {(r["instrument"],r["sequence"]): r for r in rows}
        self.assertEqual(72, len(unique))
        bad = sum(not(r["low"] <= r["open"] <= r["high"]) for r in unique.values())
        self.assertEqual(4, bad)
        self.assertEqual(dict(records=74,accepted=68,quarantined=6,unattributed=0,uncompared=0),m.expected(74))
        for stream in range(16):
            seq = sorted(r["sequence"] for r in unique.values() if r["instrument"] == f"S{stream:02d}")
            self.assertEqual(list(range(1,len(seq)+1)),seq)
    def test_exact_schedule(self):
        rows = m.schedule()
        self.assertEqual(21,len(rows))
        self.assertEqual(4,sum(v[2]=="warmup" for v in rows))
        self.assertEqual(12,sum(v[2]=="measured" for v in rows))
        self.assertEqual(2,sum(v[2]=="stress" for v in rows))
        self.assertEqual(3,sum(v[2]=="hash_reference" for v in rows))
        self.assertEqual(["A2","A3","A3","A2","A2","A3"],[v[1] for v in rows if v[0]==10000 and v[2]=="measured"])
    def test_guards(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            (root/"retained").write_bytes(b"x"*8)
            guard=m.Guard(root,seconds=30,budget=10)
            guard.check(2)
            with self.assertRaisesRegex(RuntimeError,"ARTIFACT_BUDGET"):guard.check(3)
            guard.deadline=0
            with self.assertRaisesRegex(RuntimeError,"GLOBAL_DEADLINE"):guard.remaining()
    def test_comparison_detects_changed_tail(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);a=root/"a";b=root/"b"
            a.write_bytes(b"x"*65536+b"a");b.write_bytes(b"x"*65536+b"b")
            with self.assertRaisesRegex(RuntimeError,"CANONICAL_BYTE_MISMATCH"):m.equal_files(a,b,m.Guard(root))
    def test_metrics_unknown_and_zero_cpu(self):
        value=dict(mode="A2",operationTicks=1,clockFrequency=1000,cpuTickFrequency=10000000,
                   allocatedBytes=0,cpuTicks=0,lifetimePeakWorkingSetBytes=None,peakStatus="UNKNOWN",**m.expected(74))
        m.metrics_check(value,"A2",74,"unused")
        value["accepted"]=69
        with self.assertRaisesRegex(RuntimeError,"COUNTER_MISMATCH"):m.metrics_check(value,"A2",74,"unused")
    def test_generation_hash_and_lf(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);v=m.generate(root,74,m.Guard(root))
            data=(root/"74/input.jsonl").read_bytes()
            self.assertEqual(74,data.count(b"\n"));self.assertNotIn(b"\r",data)
            manifest=json.loads((root/"74/manifest.json").read_text())
            self.assertEqual(v["inputSha256"],m.digest(root/"74/input.jsonl"))
            self.assertEqual(16,len(manifest["baselines"]))
    def test_child_timeout_is_owned_and_retained(self):
        import sys
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);guard=m.Guard(root,seconds=0.15)
            with self.assertRaisesRegex(RuntimeError,"GLOBAL_DEADLINE"):
                guard.child([sys.executable,"-c","import time;print('owned',flush=True);time.sleep(5)"],root,root/"out",root/"err")
            self.assertIn(b"owned",(root/"out").read_bytes())

if __name__=="__main__": unittest.main()
