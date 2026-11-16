"""Inert project03 consumer of the frozen producer04 source interface only.

No alternate producer-fact/admission schema. The canonical schema and its
#/$defs/consumerAdmission are hash-bound data. This source grants no SDK right.
"""
import hashlib
import json
from pathlib import Path
import re

SCHEMA_SHA='63e04fa5d270c87b93e6e1f4c38bfbcf336b950b39fe1854dc4e514f0661f14c'
SCHEMA_ID='urn:pouria:dotnet11-host-qualification:v1'
CLASSES={'win-x64':'GitHubHostedWindowsServer2025x64','linux-x64':'GitHubHostedUbuntu24.04x64'}
KINDS={'win-x64':'WindowsCIServer2025RID','linux-x64':'LinuxCIUbuntu24RID'}
CATALOGS={'win-x64':('producer04-windows-inputs.json','f87ecbf9ffebd4bcb6cd435739a070042e8c20299f63882e0277803478b54c7a'),
          'linux-x64':('producer04-linux-inputs.json','307d56b0590f5adf8bfdc03ca22d151b987774c2e49934598cfd0ac0700c0571')}
PROFILE={'projectId':'03','memoryBytes':4294967296,'cpuEquivalents':2,'processOrTasks':128,'linuxSwapBytes':0,'outerJobSeconds':1200}
CONTRACT='142dac81ae7f6e05f52319d3521d4825d5df028e37542b7623d16316ff7dd077'


class Denied(Exception):
    pass


def require(ok,code):
    if not ok:raise Denied(code)


def pinned_data(name,expected,maximum):
    path=Path(__file__).parent/name
    require(path.is_file() and not path.is_symlink() and path.stat().st_size<=maximum,'CANONICAL_INPUT_BOUND')
    raw=path.read_bytes()
    require(len(raw)<=maximum and hashlib.sha256(raw).hexdigest()==expected,'CANONICAL_INPUT_IDENTITY')
    return json.loads(raw.decode('utf-8'))


def canonical_schema():
    value=pinned_data('dotnet11-host-qualification.schema.json',SCHEMA_SHA,65536)
    require(value['$id']==SCHEMA_ID,'CANONICAL_SCHEMA_ID')
    return value


def canonical_validate(rule,value,depth=0,budget=None):
    """Only the exact keyword subset in the hash-frozen canonical source.

    No schema download, import dependency, inference or permissive fallback.
    Unsupported validation keywords fail closed; metadata is not a validator.
    """
    if budget is None:budget=[16384]
    budget[0]-=1
    require(depth<=32 and budget[0]>=0,'CANONICAL_VALIDATION_BOUND')
    require(isinstance(rule,(dict,bool)),'CANONICAL_SCHEMA_TYPE')
    if isinstance(rule,bool):require(rule,'CANONICAL_SCHEMA_DENIED');return
    known={'$schema','$id','$defs','title','description','type','const','enum','required','additionalProperties','properties','items','prefixItems','minItems','maxItems','minimum','maximum','exclusiveMinimum','minLength','maxLength','pattern'}
    require(set(rule)<=known,'CANONICAL_UNSUPPORTED_KEYWORD')
    if 'type' in rule:
        kinds=rule['type'] if isinstance(rule['type'],list) else [rule['type']]
        matches={'object':type(value) is dict,'array':type(value) is list,'string':type(value) is str,'integer':type(value) is int,'number':type(value) in (int,float),'boolean':type(value) is bool,'null':value is None}
        require(any(matches.get(kind,False) for kind in kinds),'CANONICAL_SCHEMA_DENIED')
    if 'const' in rule:require(type(value) is type(rule['const']) and value==rule['const'],'CANONICAL_SCHEMA_DENIED')
    if 'enum' in rule:require(any(type(value) is type(item) and value==item for item in rule['enum']),'CANONICAL_SCHEMA_DENIED')
    if type(value) is dict:
        require(set(rule.get('required',[]))<=set(value),'CANONICAL_SCHEMA_DENIED')
        props=rule.get('properties',{})
        if rule.get('additionalProperties') is False:require(set(value)<=set(props),'CANONICAL_SCHEMA_DENIED')
        for key,item in value.items():
            if key in props:canonical_validate(props[key],item,depth+1,budget)
    if type(value) is list:
        require(len(value)>=rule.get('minItems',0) and len(value)<=rule.get('maxItems',16384),'CANONICAL_SCHEMA_DENIED')
        prefix=rule.get('prefixItems',[])
        for index,item in enumerate(value):
            selected=prefix[index] if index<len(prefix) else rule.get('items',True)
            canonical_validate(selected,item,depth+1,budget)
    if type(value) is str:
        require(len(value)>=rule.get('minLength',0) and len(value)<=rule.get('maxLength',8388608),'CANONICAL_SCHEMA_DENIED')
        if 'pattern' in rule:require(re.search(rule['pattern'],value) is not None,'CANONICAL_SCHEMA_DENIED')
    if type(value) in (int,float):
        require(value>=rule.get('minimum',float('-inf')) and value<=rule.get('maximum',float('inf')),'CANONICAL_SCHEMA_DENIED')
        if 'exclusiveMinimum' in rule:require(value>rule['exclusiveMinimum'],'CANONICAL_SCHEMA_DENIED')


def check(fact,binding,actual):
    # Private source/controller envelope stays private; shared SDK admission is
    # exclusively the canonical consumerAdmission object nested within it.
    require(binding['status']=='ADMITTED_EXACT_SOURCE_AND_HOST_FACT','BINDING_NOT_ADMITTED')
    schema=canonical_schema();ca=binding['consumerAdmission']
    canonical_validate(schema,fact)
    canonical_validate(schema['$defs']['consumerAdmission'],ca)
    require(ca['projectId']=='03','WRONG_PROJECT_OR_CONTRACT')
    af=ca['admittedFor'];current=ca['currentExecution'];q=fact['qualifiedOn']
    rid=actual['rid'];require(rid in CLASSES,'RID_UNSUPPORTED');host=CLASSES[rid]
    require(actual['runnerEnvironment']=='github-hosted' and not actual['container'] and not actual['selfHosted'],'UNSUPPORTED_RUNNER')
    require(actual['hostClass']==host and af['hostClass']==host and af['rid']==rid,'HOST_POLICY_MISMATCH')
    require(fact['factType']==KINDS[rid] and q['hostClass']==host and q['rid']==rid,'FACT_HOST_RID_MISMATCH')
    images=binding['hostImagePolicy']
    require(actual['imageVersion'] in images['allowedImageVersions'] and actual['imageOS'] in images['allowedImageOS'],'IMAGE_NOT_ADMITTED')
    if rid=='win-x64':require(actual['windowsProductType'] in (2,3) and actual['windowsBuild']==26100,'NOT_SERVER2025')
    else:require(actual['osReleaseId']=='ubuntu' and actual['osReleaseVersion']=='24.04' and actual['uid']>0 and actual['gid']>0,'NOT_NONROOT_UBUNTU24')
    # Immutable historical tuple is never rewritten to current consumer job.
    require(q==binding['acceptedProducerFact']['qualifiedOn'],'PRODUCER_HISTORY_CHANGED')
    for key in ('runId','job','attempt','instanceId'):
        require(str(actual[{'job':'jobKey','attempt':'runAttempt','instanceId':'runnerName'}.get(key,key)])==current[key],'CONSUMER_INSTANCE_MISMATCH')
    own=binding['consumerInstance']
    for key in ('repository','runId','runAttempt','jobId','jobKey','runnerName','commit','sourceTree'):require(actual[key]==own[key],'CONSUMER_INSTANCE_MISMATCH')
    require(actual['repository']=='pouriajamali/market-data-integrity-lab','WRONG_CONSUMER_REPOSITORY')
    require(af['contractSha256']==CONTRACT and af['controllerSha256']==binding['controllerSha256'],'WRONG_PROJECT_OR_CONTRACT')
    require(af['sourcePinTableSha256']==binding['sourcePinTableSha256'] and af['profileSha256']==binding['profileSha256'],'OWN_SOURCE_PROFILE_BINDING')
    require(af['memberManifestSha256']==fact['tree']['memberManifestSha256'],'FACT_MEMBER_IDENTITY')
    name,catalog_pin=CATALOGS[rid];catalog=pinned_data(name,catalog_pin,8*1024*1024)
    require(fact['source']['schemaSha256']==SCHEMA_SHA and fact['source']['catalogSha256']==catalog_pin,'FACT_SOURCE_INTERFACE_IDENTITY')
    for key in ('url','bytes','sha512'):require(fact['archive'][key]==catalog[key],'FACT_ARCHIVE_IDENTITY')
    require(fact['tree']['memberManifestSha256']==catalog['memberManifestSha256'],'FACT_MEMBER_IDENTITY')
    proof=actual['ownControlProof']
    require(binding['profile']==PROFILE and proof['effectiveProfile']==PROFILE,'OWN_PROFILE_MISMATCH')
    require(proof['projectId']=='03' and proof['runId']==actual['runId'] and proof['jobId']==actual['jobId'],'BORROWED_CONTROL_PROOF')
    require(proof['sourceSha256']==af['controllerSha256'] and proof['evidenceSha256']==current['ownCapsEvidenceSha256'],'OWN_CONTROL_IDENTITY')
    require(proof['heldChildBeforeTargets'],'OWN_KERNEL_PROOF_REQUIRED')
    return {'decision':'CANONICAL_TYPED_INPUTS_MATCH_NOT_SDK_CAPABILITY','producerQualifiedOn':q,'consumerCurrentExecution':current,'hostClass':host,'rid':rid,'producerKernelProofTransferred':False,'globalSDK_READY':False}


def verify_fact_bytes(raw,binding):
    require(len(raw)<=8*1024*1024,'FACT_SIZE')
    require(hashlib.sha256(raw).hexdigest()==binding['consumerAdmission']['producerFactSha256'],'FACT_DIGEST')
    def pairs(items):
        value={}
        for key,item in items:require(key not in value,'FACT_DUPLICATE_KEY');value[key]=item
        return value
    return json.loads(raw.decode('utf-8'),object_pairs_hook=pairs)


def consume_after_checks(fact,binding,actual,fixed_consumer):
    return fixed_consumer(check(fact,binding,actual))


def bind_current_execution(policy,actual):
    """Proposed runtime factory AFTER own actual API/held-kernel observations.

    Manager policy pins fixed source/fact/host/profile first. This does not
    fabricate currentExecution in the inert source-only policy document.
    """
    require(policy['status']=='ADMITTED_EXACT_SOURCE_AND_HOST_FACT','BINDING_NOT_ADMITTED')
    for key in ('repository','commit','sourceTree','jobKey'):
        require(actual[key]==policy['expectedConsumerSource'][key],'CONSUMER_SOURCE_MISMATCH')
    ca=json.loads(json.dumps(policy['consumerAdmissionBase']))
    ca['currentExecution']={'runId':str(actual['runId']),'job':actual['jobKey'],'attempt':str(actual['runAttempt']),'instanceId':actual['runnerName'],'ownCapsEvidenceSha256':actual['ownControlProof']['evidenceSha256']}
    canonical_validate(canonical_schema()['$defs']['consumerAdmission'],ca)
    result=dict(policy);result['consumerAdmission']=ca
    result['consumerInstance']={k:actual[k] for k in ('repository','runId','runAttempt','jobId','jobKey','runnerName','commit','sourceTree')}
    return result
