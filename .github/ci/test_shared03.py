"""Only changed pure values: no SDK, process, network or shared import."""
import copy,json
from sdk03_binding import construct
from typed_host_03 import Denied
def run():
    a={'repository':'pouriajamali/market-data-integrity-lab','commit':'a'*40,'sourceTree':'b'*40,'jobKey':'consumer03','rid':'linux-x64','runId':27,'runAttempt':2,'runnerName':'consumer-instance'}
    c={'schema':'sdk11-own-current-cap-evidence/v1','projectId':'03','repository':a['repository'],'sourceCommit':a['commit'],'sourceTree':a['sourceTree'],'profileSha256':'c'*64,'controllerSha256':'d'*64,'worker':{'pid':51,'processStartIdentity':'proc-start:121','uid':1001,'gid':1001,'kernelLimits':{'memory.max':'4294967296','memory.swap.max':'0','pids.max':'128','cpu.max':'200000 100000'}},'beforeExec':{'pid':51,'processStartIdentity':'proc-start:121','state':'T','membershipBeforeRelease':True,'retainedStartHandle':True,'kernelLimits':{'memory.max':'4294967296','memory.swap.max':'0','pids.max':'128','cpu.max':'200000 100000'}},'supervisor':{'pid':45,'processStartIdentity':'proc-start:100','deadlineMonotonicNs':1110000000001},'rowStartedMonotonicNs':1,'deadlineMonotonicNs':1110000000001}
    p={'status':'ADMITTED_EXACT_SOURCE_AND_HOST_FACT','expectedConsumerSource':{k:a[k] for k in ('repository','commit','sourceTree','jobKey')},'consumerAdmissionBase':{'schema':'sdk11-consumer-host-admission/v1','state':'ADMITTED_EXACT_SUPPORTED_HOST_CLASS','projectId':'03','producerFactSha256':'757abf2d3dde65d10cd895cfc97eaeccb7fc11e8685b94ad046e6a8ec8829007','admittedFor':{'hostClass':'GitHubHostedUbuntu24.04x64','rid':'linux-x64','sdk':'11.0.100','runtime':'11.0.0','memberManifestSha256':'e'*64,'contractSha256':'f'*64,'sourcePinTableSha256':'1'*64,'controllerSha256':'d'*64,'profileSha256':'c'*64},'identityRule':'QUALIFIED_ON_IS_HISTORICAL_PRODUCER_INSTANCE_CURRENT_CONSUMER_JOB_MAY_DIFFER'}}
    paths={k:'/synthetic/'+k for k in ('controller','contract','profile','sourcePins','checkout','admission','capsEvidence')}
    cases=[('different-consumer-instance',None,None,None,True),('windows',a,'rid','win-x64',False),('wrong-owner',c,'projectId','04',False),('root',c['worker'],'uid',0,False),('borrowed64',c['worker']['kernelLimits'],'pids.max','64',False),('not-held',c['beforeExec'],'state','R',False),('no-retained-handle',c['beforeExec'],'retainedStartHandle',False,False),('deadline-reset',c,'deadlineMonotonicNs',1201000000000,False),('source',a,'commit','0'*40,False),('inactive',p,'status','INERT',False),('controller',c,'controllerSha256','0'*64,False)]
    results=[]
    for name,obj,key,value,want in cases:
        if obj is not None:old=obj[key];obj[key]=value
        try:construct(p,a,c,paths);ok=True
        except Denied:ok=False
        finally:
            if obj is not None:obj[key]=old
        if ok!=want:raise RuntimeError(name)
        results.append({'case':name,'observed':ok,'expected':want})
    print(json.dumps({'scope':'SYNTHETIC_CHANGED_WIRING_ONLY','native':False,'SDK':0,'cases':results}))
if __name__=='__main__':run()
