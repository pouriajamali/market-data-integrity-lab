# Claim-to-evidence map

| Supported claim | Inspectable source/tests | Practical limit |
| --- | --- | --- |
| Oversized records are drained and raw-hashed with bounded retention | [BoundedReader.cs](../src/Integrity/BoundedReader.cs); [BehaviorTests.PhysicalProvenanceIncludesCrAndEofRecordWithoutInventingTrailingLine](../tests/Integrity.Tests/BehaviorTests.cs) | Output/disk growth is not bounded by R |
| A2 uses explicit baselines, bounded consumed windows and persistent gaps/conflicts | [SequenceEngine.cs](../src/Integrity/SequenceEngine.cs); [SequenceTests.HandwrittenTransitionOracleChecksWindowConsumptionAndPersistentConflict](../tests/Integrity.Tests/SequenceTests.cs); GapDoesNotHealAndOtherDeclaredStreamsAndEpochsContinue | No feed completeness or inferred reset |
| A1/A2 published synthetic bytes stay compatible | [SequenceTests.A1PublishedSyntheticBytesAndOutputsRemainExactlyPinned](../tests/Integrity.Tests/SequenceTests.cs); [RecoveryBoundsTests.PublishedA2SyntheticCanonicalBytesRemainPinned](../tests/Integrity.Tests/RecoveryBoundsTests.cs) | Specific golden fixtures |
| Recovery validates slot identity/state and sink prefixes before resume | [RecoveryRunner.cs](../src/Integrity/RecoveryRunner.cs) / [RecoveryCheckpoint.cs](../src/Integrity/RecoveryCheckpoint.cs); [RecoveryTests.ChangedIdentityOrFutureVersionCannotAuthorizeFallback](../tests/Integrity.Tests/RecoveryTests.cs); InvalidLatestCandidateFallsBackOnlyToFullyCompatiblePrevious | Local cooperating writers, locking enabled |
| Completed recovery preserves all three A2 canonical files | [RecoveryTests.FiniteRealKillMatrixResumesToExactA2Bytes](../tests/Integrity.Tests/RecoveryTests.cs); ProductionCliStartAndCompletedResumeHaveExactCanonicalA2Output | Finite owned-child cuts, not power-loss durability |
| Evidence failure does not authorize truncation | [RecoveryTests.PartialEvidenceFailureCannotTruncateAndRequiresManualArchive](../tests/Integrity.Tests/RecoveryTests.cs); EvidenceByteBudgetRefusesNewCopiesBeforeCanonicalMutation | Recovery can refuse until manual archive |
| Fixed C# regression cases pass Windows and Linux | Actual A3 CI41643617892, tag41643951656, commit335a04e | CI confirms that source/cases, not production or personal history |
| A4 metric/recipe/resource logic is inspectable | tools/Measurements/Program.cs; tools/run_measurements.py; tools/test_measurements.py; tools/check_measurements.py | Separate fresh processes; one fixed workload/host and no isolated causal overhead claim |
| Measured mode costs and raw samples | [Raw receipt](evidence/measurements/results.json), [independent summary](evidence/measurements/summary.json), [analysis source](../tools/analyze_measurements.py) | One Windows host,3 regular repetitions,1 stress observation, no causal isolated overhead or tail SLA |

Development receipts are pre-release local evidence; exact source tests and
linked CI are the public reproducible evidence. CV/profile files are not part of
this repository or publication.

[Published A3 main CI](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/41643617892) and [tag CI](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/41643951656) establish the existing80-case Windows/Linux baseline. Final release CI is a separate exact-commit gate, verified during publication.

B1 adds a fresh-only synthetic demo; [DemoTests](../tests/Integrity.Tests/DemoTests.cs)
check golden bytes, existing-root no-mutation, usage/exit meanings and retained
partial evidence on presentation failure. Package manifests and extracted native
apphost checks cover the delivered binaries. Historical A4 timings do not apply
to B1; ready demos do not execute the crash probe.
