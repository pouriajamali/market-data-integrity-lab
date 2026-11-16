"""Only pure data wiring. No network, process, SDK or admission authority."""
import hashlib
import json
from typed_host_03 import require, canonical_validate, canonical_schema
CONTEXT_KEYS={'projectId','repository','checkout','controller','contract','profile','sourcePins','admission','capsEvidence','expectedSourceCommit','expectedSourceTree','rowStartedMonotonicNs','deadlineMonotonicNs','supervisorPID','supervisorStartIdentity'}
def canonical(v):return json.dumps(v,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
def construct(policy,actual,caps,paths):
    require(policy['status']=='ADMITTED_EXACT_SOURCE_AND_HOST_FACT','POLICY_NOT_ADMITTED')
    require(actual['rid']=='linux-x64','WINDOWS_NORMAL_DENIED')
    require(actual['repository']=='pouriajamali/market-data-integrity-lab','OWN_REPOSITORY')
    require(policy['expectedConsumerSource']=={k:actual[k] for k in ('repository','commit','sourceTree','jobKey')},'OWN_SOURCE')
    require(caps['projectId']=='03' and caps['repository']==actual['repository'] and caps['sourceCommit']==actual['commit'] and caps['sourceTree']==actual['sourceTree'],'OWN_CAPS')
    require(caps['worker']['pid']==caps['beforeExec']['pid'] and caps['worker']['processStartIdentity']==caps['beforeExec']['processStartIdentity'],'OWN_HELD')
    require(caps['worker']['uid']>0 and caps['worker']['gid']>0 and caps['beforeExec']['state']=='T' and caps['beforeExec']['membershipBeforeRelease'] and caps['beforeExec']['retainedStartHandle'],'OWN_HELD')
    require(caps['worker']['kernelLimits']=={'memory.max':'4294967296','memory.swap.max':'0','pids.max':'128','cpu.max':'200000 100000'} and caps['beforeExec']['kernelLimits']==caps['worker']['kernelLimits'],'OWN_LIMITS')
    start=caps['rowStartedMonotonicNs'];end=caps['deadlineMonotonicNs']
    require(type(start) is int and type(end) is int and 0<end-start<=1200*10**9 and caps['supervisor']['deadlineMonotonicNs']==end,'ORIGINAL_DEADLINE')
    admission=json.loads(json.dumps(policy['consumerAdmissionBase']))
    require(admission['projectId']=='03' and admission['producerFactSha256']=='757abf2d3dde65d10cd895cfc97eaeccb7fc11e8685b94ad046e6a8ec8829007','OWN_FACT')
    require(admission['admittedFor']['profileSha256']==caps['profileSha256'] and admission['admittedFor']['controllerSha256']==caps['controllerSha256'],'OWN_BINDING')
    admission['currentExecution']={'runId':str(actual['runId']),'job':actual['jobKey'],'attempt':str(actual['runAttempt']),'instanceId':actual['runnerName'],'ownCapsEvidenceSha256':hashlib.sha256(canonical(caps)).hexdigest()}
    canonical_validate(canonical_schema()['$defs']['consumerAdmission'],admission)
    context=dict(paths);context.update(projectId='03',repository=actual['repository'],expectedSourceCommit=actual['commit'],expectedSourceTree=actual['sourceTree'],rowStartedMonotonicNs=start,deadlineMonotonicNs=end,supervisorPID=caps['supervisor']['pid'],supervisorStartIdentity=caps['supervisor']['processStartIdentity'])
    require(set(context)==CONTEXT_KEYS,'EXACT_CONTEXT')
    return admission,context
