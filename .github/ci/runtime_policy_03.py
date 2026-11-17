"""Own03 current admission binding; the shared SDK APIs stay byte-identical."""
import hashlib,json,os,subprocess
from pathlib import Path
from typed_host_03 import require,canonical_schema,canonical_validate

def canonical(v):return json.dumps(v,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def bind(repo,own,actual,caps):
    rid=actual['rid'];require(rid in ('linux-x64','win-x64'),'OWN_RID')
    profile=repo/'.github/ci'/('integrity03-windows-profile.json' if rid=='win-x64' else 'integrity03-profile.json')
    controller=repo/'.github/ci/host_controller03.py';contract=repo/'.github/ci/integrity03-route-contract.json';pins=repo/'.github/ci/integrity03-source-pins.json'
    fact_path=repo/'tools/sdk11-normal'/('accepted-windows-fact.json' if rid=='win-x64' else 'accepted-linux-fact.json');fact=json.loads(fact_path.read_bytes())
    require(actual['repository']=='pouriajamali/market-data-integrity-lab' and caps['projectId']=='03' and caps['repository']==actual['repository'],'OWN_REPOSITORY')
    require(caps['controllerSha256']==digest(controller) and caps['profileSha256']==digest(profile) and caps['sourceCommit']==actual['commit'] and caps['sourceTree']==actual['sourceTree'],'OWN_CURRENT_SOURCE')
    held=caps['heldBeforeExec'] if rid=='win-x64' else caps['beforeExec']
    require(held['membershipBeforeRelease'] and held['retainedStartHandle'] and held['pid']==caps['worker']['pid'],'OWN_HELD_PROOF')
    require(caps['deadlineMonotonicNs']==actual['deadlineMonotonicNs'] and caps['rowStartedMonotonicNs']==actual['rowStartedMonotonicNs'],'OWN_ORIGINAL_CLOCK')
    raw_caps=canonical(caps);cap_path=own/'control-proof.json';require(cap_path.read_bytes()==raw_caps,'OWN_CAP_BYTES')
    admission={'schema':'sdk11-consumer-host-admission/v1','state':'ADMITTED_EXACT_SUPPORTED_HOST_CLASS','projectId':'03','producerFactSha256':digest(fact_path),'admittedFor':{'hostClass':fact['qualifiedOn']['hostClass'],'rid':rid,'sdk':'11.0.100','runtime':'11.0.0','memberManifestSha256':fact['tree']['memberManifestSha256'],'contractSha256':digest(contract),'sourcePinTableSha256':hashlib.sha256(canonical(json.loads(pins.read_bytes())['sourcePins'])).hexdigest(),'controllerSha256':digest(controller),'profileSha256':digest(profile)},'currentExecution':{'runId':str(actual['runId']),'job':actual['jobKey'],'attempt':str(actual['runAttempt']),'instanceId':actual['runnerName'],'ownCapsEvidenceSha256':hashlib.sha256(raw_caps).hexdigest()},'identityRule':'QUALIFIED_ON_IS_HISTORICAL_PRODUCER_INSTANCE_CURRENT_CONSUMER_JOB_MAY_DIFFER'}
    canonical_validate(canonical_schema()['$defs']['consumerAdmission'],admission)
    path=own/'consumer-admission.json';path.write_bytes(canonical(admission))
    return {'projectId':'03','repository':actual['repository'],'checkout':str(repo),'controller':str(controller),'contract':str(contract),'profile':str(profile),'sourcePins':str(pins),'admission':str(path),'capsEvidence':str(cap_path),'expectedSourceCommit':actual['commit'],'expectedSourceTree':actual['sourceTree'],'rowStartedMonotonicNs':caps['rowStartedMonotonicNs'],'deadlineMonotonicNs':caps['deadlineMonotonicNs'],'supervisorPID':caps['supervisor']['pid'],'supervisorStartIdentity':caps['supervisor']['processStartIdentity']}

def stop_observer(context):
    """Nonroot ExecStopPost witnesses the drained group before collection."""
    import signal,time
    own=Path(context['temp']);require(os.getuid()==context['actual']['uid'] and os.getgid()==context['actual']['gid'],'STOP_OWNER')
    cgline=next(x[3:] for x in Path('/proc/self/cgroup').read_text().splitlines() if x.startswith('0::'))
    require(cgline=='/system.slice/'+context['unit']+'.service','STOP_OWN_GENERATION')
    root=Path('/sys/fs/cgroup')/cgline.lstrip('/');pid=os.getpid();start=Path('/proc/self/stat').read_text().rsplit(')',1)[1].split()[19]
    pids=[int(x) for x in (root/'cgroup.procs').read_text().split()];require(pids==[pid],'STOP_REMAINING_CHILDREN')
    limits={k:(root/k).read_text().strip() for k in ('memory.max','memory.swap.max','pids.max','cpu.max')}
    require(limits=={'memory.max':'4294967296','memory.swap.max':'0','pids.max':'128','cpu.max':'200000 100000'},'STOP_OWN_LIMITS')
    record={'pid':pid,'start':start,'unit':context['unit'],'cgroup':cgline,'remainingPopulationExcludingObserver':0,'limits':limits,'state':'HELD_STOP_OBSERVER','observedNs':time.monotonic_ns()}
    (own/'stop-held.json').write_bytes(canonical(record));os.kill(pid,signal.SIGSTOP)
    (own/'stop-complete.json').write_bytes(canonical(dict(record,state='STOP_OBSERVER_EXITING')))
