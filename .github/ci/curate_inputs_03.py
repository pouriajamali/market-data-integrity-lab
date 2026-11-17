"""Fixed Integrity input/lock stage inside the existing own capped worker.

Uses only the already qualified shared SDK path. This is not an SDK installer,
shared qualifier or a general command/feed interface. Product targets stay in
native_worker_03 and cannot run from this input-only stage.
"""
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import shutil
import subprocess
import time
import urllib.request
import zipfile
import xml.etree.ElementTree as ET
from typed_host_03 import require

PROJECTS=('src/Integrity/Integrity.csproj','tests/Integrity.Tests/Integrity.Tests.csproj','tests/RecoveryProbe/RecoveryProbe.csproj','tools/Measurements/Measurements.csproj')

def verification_report(text,rows,code):
    require(code==0 and len(text.encode())<=8*1024*1024,'OWN_NUGET_VERIFY_EXIT_OR_SIZE')
    require(not re.search(r'(?im)^.*\b(?:warning|error|NU[0-9]{4})\b|revocation[^\n]*unknown|verification failed',text),'OWN_NUGET_VERIFY_WARNING_OR_UNKNOWN')
    role_lines={'Verifying the author primary signature with certificate:',"Verifying author primary signature's timestamp with timestamping service certificate:",'Verifying the repository countersignature with certificate:',"Verifying repository countersignature's timestamp with timestamping service certificate:", 'Verifying the repository primary signature with certificate:',"Verifying repository primary signature's timestamp with timestamping service certificate:"}
    lines=[s.strip() for s in text.replace('\r\n','\n').splitlines()]
    starts=[i for i,s in enumerate(lines) if s.startswith('Verifying ') and s not in role_lines]
    require(len(starts)==len(rows),'OWN_NUGET_VERIFY_PACKAGE_COUNT')
    out=[]
    for n,row in enumerate(rows):
        block=lines[starts[n]:starts[n+1] if n+1<len(starts) else len(lines)]
        identity=row['id']+'.'+row['version'];require(block[0].casefold()==('Verifying '+identity).casefold(),'OWN_NUGET_VERIFY_IDENTITY')
        hashes=[s.removeprefix('Content hash: ') for s in block if s.startswith('Content hash: ')]
        successes=[s for s in block if s.startswith('Successfully verified package')]
        require(len(hashes)==1 and successes==["Successfully verified package '"+identity+"'."],'OWN_NUGET_VERIFY_HASH_OR_SUCCESS')
        require(len(base64.b64decode(hashes[0],validate=True))==64 and hashes[0]==row['contentHash'],'OWN_NORMALIZED_LOCK_HASH_MISMATCH')
        require(any(s.startswith('Signature type: ') for s in block) and any(s.startswith('SHA256 hash: ') for s in block),'OWN_CMS_SIGNATURE_REPORT_REQUIRED')
        out.append({'id':row['id'],'version':row['version'],'normalizedNuGetContentHash':hashes[0],'actualNativeVerificationExit':code})
    return out

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs):return None

def fetch_exact(row,destination,end):
    expected='https://api.nuget.org/v3-flatcontainer/'+row['id'].lower()+'/'+row['version']+'/'+row['id'].lower()+'.'+row['version']+'.nupkg'
    require(row['url']==expected and 0<row['bytes']<=128*1024*1024,'OWN_FIXED_PACKAGE_URL_OR_SIZE')
    remaining=(end-time.monotonic_ns())/10**9;require(remaining>0,'ORIGINAL_INPUT_DEADLINE')
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),NoRedirect())
    request=urllib.request.Request(expected,headers={'Accept-Encoding':'identity','User-Agent':'Integrity03-exact-input-consumer'})
    count=0;sha=hashlib.sha256();sha512=hashlib.sha512()
    with opener.open(request,timeout=min(10,remaining)) as response,destination.open('xb') as output:
        cl=response.headers.get_all('Content-Length',[]);te=response.headers.get_all('Transfer-Encoding',[])
        require(response.status==200 and response.geturl()==expected and not response.headers.get('Content-Encoding'),'OWN_PACKAGE_HTTP_IDENTITY')
        require((len(cl)==1 and not te and cl[0]==str(row['bytes'])) or (not cl and len(te)==1 and te[0].lower()=='chunked'),'OWN_PACKAGE_HTTP_FRAMING')
        while True:
            require(time.monotonic_ns()<end,'ORIGINAL_INPUT_DEADLINE');block=response.read(min(65536,row['bytes']+1-count))
            if not block:break
            count+=len(block);require(count<=row['bytes'],'OWN_PACKAGE_BODY_CAP');sha.update(block);sha512.update(block);output.write(block)
    require(count==row['bytes'] and sha.hexdigest()==row['sha256'] and base64.b64encode(sha512.digest()).decode()==row['rawSHA512Base64'],'OWN_SIGNED_RAW_HASH')
    return {'bytes':count,'sha256':sha.hexdigest(),'sha512Base64':base64.b64encode(sha512.digest()).decode()}

def private_environment(sdk,own):
    temp=own/'temp';temp.mkdir(exist_ok=True,mode=0o700)
    env={k:v for k,v in os.environ.items() if k in ('SystemRoot','WINDIR','COMSPEC','LANG','LC_ALL')}
    env.update(PATH=str(sdk)+os.pathsep+('/usr/bin:/bin' if os.name!='nt' else os.environ.get('PATH','')),HOME=str(own),USERPROFILE=str(own),DOTNET_ROOT=str(sdk),DOTNET_HOST_PATH=str(sdk/('dotnet.exe' if os.name=='nt' else 'dotnet')),DOTNET_CLI_HOME=str(own),NUGET_PACKAGES=str(own/'packages'),TMPDIR=str(temp),TEMP=str(temp),TMP=str(temp),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true',DOTNET_NOLOGO='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_EnableDiagnostics='0',COMPlus_EnableDiagnostics='0',DOTNET_CLI_USE_MSBUILD_SERVER='0',MSBUILDDISABLENODEREUSE='1',MSBuildEnableWorkloadResolver='false',DOTNET_NUGET_SIGNATURE_VERIFICATION='true',NUGET_CERT_REVOCATION_MODE='online',DOTNET_CLI_UI_LANGUAGE='en-US')
    if os.name=='nt':
        for key in ('PROGRAMFILES','PROGRAMFILES(X86)','APPDATA','LOCALAPPDATA'):
            path=own/key.replace('(','').replace(')','');path.mkdir(exist_ok=True);env[key]=str(path)
    return env

def curate(repo,own,sdk,actual,catalog,stage):
    require(stage in ('input-curation','product') and catalog['rid']==actual['rid'],'OWN_INPUT_STAGE_OR_RID')
    require(catalog['state']=='SOURCE_BOUND_CANDIDATES_REQUIRING_OWN_NATIVE_VERIFICATION','OWN_INPUT_SOURCE_STATE')
    require(-300<=time.time()-catalog['advisory']['observedAtUnix']<=catalog['advisory']['maxAgeSeconds']<=86400,'OWN_ADVISORY_SNAPSHOT_STALE')
    rows=catalog['packages'];require(19<=len(rows)<=20 and len({r['id'].lower() for r in rows})==len(rows) and sum(r['bytes'] for r in rows)<=268435456,'OWN_FINITE_PACKAGE_INPUTS')
    require(all(re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,99}',r['id']) and re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+',r['version']) for r in rows),'OWN_FIXED_PACKAGE_NAMES')
    native_kind='accepted-shared-'+('windows' if actual['rid']=='win-x64' else 'linux')+'-native'
    native={r['id'] for r in rows if r['kind']==native_kind}
    allowed={'Microsoft.NETCore.App.Runtime.'+actual['rid'],'Microsoft.AspNetCore.App.Runtime.'+actual['rid'],'Microsoft.NETCore.App.Host.'+actual['rid']}
    require(len([r for r in rows if r['kind']=='fixed-M1-test-dependency'])==17 and native==set(catalog['actualImplicitRuntimeDownloadIds']) and native<=allowed and 'Microsoft.NETCore.App.Runtime.'+actual['rid'] in native,'OWN_SHARED_INPUT_SCOPE')
    require(catalog['advisory']['state']=='NO_TEST_ID_PRESENT_AND_SHARED_NATIVE_VERSION_NO_MATCH','OWN_QUALIFIED_ADVISORY')
    if actual['rid']=='linux-x64':require(catalog['sharedInputFactSHA256']=='b94a8d74cd6290b17c2e063d0d15ddab04c086f5a9b41120e5c53ee01c7671df','OWN_QUALIFIED_LINUX_NATIVE_FACT')
    end=actual['deadlineMonotonicNs']-50*10**9;require(time.monotonic_ns()<end,'OWN_INPUT_TERMINAL_RESERVE')
    evidence=repo/'artifacts/input-curation';evidence.mkdir(parents=True,exist_ok=False)
    feed=own/'feed';feed.mkdir(mode=0o700);notices=evidence/'original-notices';notices.mkdir()
    raw=[]
    for row in rows:
        path=feed/(row['id'].lower()+'.'+row['version']+'.nupkg');raw.append(fetch_exact(row,path,end))
        with zipfile.ZipFile(path) as archive:
            names=archive.namelist();require(len(names)<=4096 and len(set(names))==len(names) and '.signature.p7s' in names,'OWN_SIGNED_ARCHIVE_NAMESPACE')
            require(all(not n.startswith('/') and '\\' not in n and ':' not in n and '..' not in Path(n).parts and all(ord(c)>=32 for c in n) for n in names),'OWN_ARCHIVE_PATH')
            require(sum(i.file_size for i in archive.infolist())<=536870912 and all(i.file_size<=67108864 for i in archive.infolist()),'OWN_ARCHIVE_EXPANDED_CAP')
            require(all(stat.S_IFMT(i.external_attr>>16) in (0,stat.S_IFREG,stat.S_IFDIR) for i in archive.infolist()),'OWN_ARCHIVE_SPECIAL_FILE')
            for name in names:
                if archive.getinfo(name).is_dir():continue
                if name.endswith('.nuspec') or any(s in Path(name).name.lower() for s in ('license','notice','copying')):
                    require(archive.getinfo(name).file_size<=1048576,'OWN_NOTICES_BOUND');dest=notices/row['id']/name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(archive.read(name))
    # Exact official repository signing history captured in the qualified input
    # set. Historical signatures retain their trusted timestamp validation.
    repository_bytes=(repo/'tools/sdk11-normal/native-linux/metadata/repository.json').read_bytes()
    require(hashlib.sha256(repository_bytes).hexdigest()=='5f45cbf2d7f8a4b57fea6e43bb603d29a0682058a71eb61d900db74860d210b0','OWN_REPOSITORY_SIGNING_METADATA')
    repository=json.loads(repository_bytes)
    certificates=[c['fingerprints']['2.16.840.1.101.3.4.2.1'] for c in repository['signingCertificates']]
    require(repository['allRepositorySigned'] is True and len(certificates)==3 and all(re.fullmatch('[0-9a-f]{64}',v) for v in certificates),'OWN_REPOSITORY_CERTIFICATE_SET')
    tree=ET.Element('configuration');sources=ET.SubElement(tree,'packageSources');ET.SubElement(sources,'clear');ET.SubElement(sources,'add',key='verified-own-inputs',value=str(feed))
    fallback=ET.SubElement(tree,'fallbackPackageFolders');ET.SubElement(fallback,'clear')
    settings=ET.SubElement(tree,'config');ET.SubElement(settings,'add',key='signatureValidationMode',value='require')
    signers=ET.SubElement(tree,'trustedSigners');ET.SubElement(signers,'clear');signer=ET.SubElement(signers,'repository',name='nuget.org',serviceIndex='https://api.nuget.org/v3/index.json')
    for certificate in certificates:ET.SubElement(signer,'certificate',fingerprint=certificate,hashAlgorithm='SHA256',allowUntrustedRoot='false')
    config=own/'offline.nuget.config';config.write_bytes(ET.tostring(tree,encoding='utf-8',xml_declaration=True))
    env=private_environment(sdk,own);dotnet=sdk/('dotnet.exe' if os.name=='nt' else 'dotnet');commands=[]
    work=repo
    if stage=='input-curation':
        work=own/'graph';work.mkdir(mode=0o700)
        for name in ('Directory.Build.props','global.json',*PROJECTS,*(str(Path(p).parent/'packages.lock.json') for p in PROJECTS)):
            src=repo/name;dest=work/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dest)
    def run(label,args,seconds):
        remaining=(end-time.monotonic_ns())/10**9;require(remaining>0,'ORIGINAL_INPUT_DEADLINE')
        output=evidence/(label+'.stdout');error=evidence/(label+'.stderr')
        with output.open('xb') as so,error.open('xb') as se:
            process=subprocess.Popen([str(dotnet),*[str(x) for x in args]],cwd=work,env=env,stdout=so,stderr=se)
            deadline=min(end,time.monotonic_ns()+seconds*10**9)
            while process.poll() is None:
                require(time.monotonic_ns()<deadline and output.stat().st_size+error.stat().st_size<=8388608,'OWN_INPUT_COMMAND_DEADLINE_OR_LOG_CAP');time.sleep(.02)
        require(process.returncode==0,'OWN_INPUT_COMMAND_'+label.upper());commands.append({'name':label,'exitCode':process.returncode,'stdoutSHA256':hashlib.sha256(output.read_bytes()).hexdigest(),'stderrSHA256':hashlib.sha256(error.read_bytes()).hexdigest()});return output.read_text(encoding='utf-8-sig')
    own_rows=[r for r in rows if r['kind']=='fixed-M1-test-dependency']
    acceptance_binding=catalog['testInputAcceptance'];acceptance_path=repo/acceptance_binding['file']
    require(acceptance_path.resolve().is_relative_to(repo.resolve()) and hashlib.sha256(acceptance_path.read_bytes()).hexdigest()==acceptance_binding['sha256'],'OWN_FIXED_TEST_INPUT_ACCEPTANCE_SOURCE')
    acceptance=json.loads(acceptance_path.read_bytes())
    require(acceptance['state']=='ACTUAL_SEVENTEEN_FROZEN_TEST_INPUT_SIGNATURES_ACCEPTED' and acceptance['nativeVerificationExit']==0 and acceptance['warningOrUnknownAccepted'] is False and acceptance['runId']==44096474043 and acceptance['packages']==own_rows,'OWN_FIXED_TEST_INPUT_ACCEPTANCE')
    verified=[{'id':r['id'],'version':r['version'],'normalizedNuGetContentHash':r['contentHash'],'reusedOwnExactInputAcceptanceSHA256':acceptance_binding['sha256'],'qualifiedOnRid':acceptance['rid'],'newQualificationPerformed':False} for r in own_rows]
    verified.extend({'id':r['id'],'version':r['version'],'normalizedNuGetContentHash':r['contentHash'],'reusedQualifiedProducerFactSHA256':catalog['sharedInputFactSHA256'],'newSharedQualificationPerformed':False} for r in rows if r['kind']==native_kind)
    if stage=='product':
        return {'dotnet':str(dotnet),'python':os.sys.executable,'privateEnvironment':env,'offlineConfig':str(config),'admittedRidLock':str(repo/'build/locks'/('Integrity.'+actual['rid']+'.lock.json')),'runtimePack':str(own/'packages'/('microsoft.netcore.app.runtime.'+actual['rid'])/'11.0.0'),'inputReceipt':verified,'nativeRows':[r for r in rows if r['kind']==native_kind],'privateFeed':str(feed)}
    for project in ('tests/Integrity.Tests/Integrity.Tests.csproj','tools/Measurements/Measurements.csproj'):
        args=['restore',project,'--configfile',config,'--packages',own/'packages','-p:NuGetAudit=false']
        args+=['--force-evaluate','--use-lock-file'] if stage=='input-curation' else ['--locked-mode']
        run('restore-'+Path(project).parent.name,args,90)
    lock=evidence/(actual['rid']+'.lock.json') if stage=='input-curation' else repo/'build/locks'/('Integrity.'+actual['rid']+'.lock.json')
    args=['restore','src/Integrity/Integrity.csproj','-r',actual['rid'],'--configfile',config,'--packages',own/'packages','-p:NuGetAudit=false','-p:SelfContained=true','-p:RuntimeFrameworkVersion=11.0.0','-p:NuGetLockFilePath='+str(lock)]
    args+=['--force-evaluate','--use-lock-file'] if stage=='input-curation' else ['--locked-mode'];run('restore-rid',args,90)
    graphs=[]
    for project in PROJECTS:
        asset=work/Path(project).parent/'obj/project.assets.json';value=json.loads(asset.read_bytes())
        projection={'project':project,'assetSHA256':hashlib.sha256(asset.read_bytes()).hexdigest(),'frameworks':{}}
        for framework,body in value['project']['frameworks'].items():
            downloads=body.get('downloadDependencies',[])
            for dep in downloads:
                expected=next((r for r in rows if r['id'].casefold()==dep['name'].casefold()),None)
                require(expected and dep['version'] in (expected['version'],'['+expected['version']+', '+expected['version']+']','['+expected['version']+']'),'OWN_UNEXPECTED_IMPLICIT_SDK_DOWNLOAD')
            projection['frameworks'][framework]={'downloadDependencies':downloads,'frameworkReferences':body.get('frameworkReferences',{}),'runtimeIdentifierGraphPath':body.get('runtimeIdentifierGraphPath')}
        graphs.append(projection)
    (evidence/'actual-sdk-graphs.json').write_text(json.dumps(graphs,indent=2)+'\n',encoding='utf-8')
    locks=[]
    for project in PROJECTS:
        path=work/Path(project).parent/'packages.lock.json';value=json.loads(path.read_bytes());require(set(value['dependencies'])=={'net11.0'},'OWN_NET11_LOCK_FRAMEWORK')
        for key,row in value['dependencies']['net11.0'].items():
            if row['type']=='Project':continue
            expected=next((r for r in rows if r['id'].lower()==key.lower()),None);require(expected and row['resolved']==expected['version'] and row['contentHash']==expected['contentHash'],'OWN_UNEXPECTED_LOCK_GRAPH_OR_HASH')
        target=evidence/Path(project).parent/'packages.lock.json';target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(path.read_bytes());locks.append({'path':str(Path(project).parent/'packages.lock.json'),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    receipt={'scope':'OWN_FIXED_INPUT_HASH_NOTICES_ADVISORY_AND_ACTUAL_GRAPH','rid':actual['rid'],'sourceCommit':actual['commit'],'commands':commands,'inputs':verified,'rawInputs':raw,'locks':locks,'productExecuted':False,'otherRidRequiredForDelivery':True,'SDK_READY':False}
    (evidence/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
    return {'dotnet':str(dotnet),'python':os.sys.executable,'privateEnvironment':env,'admittedRidLock':str(lock),'runtimePack':str(own/'packages'/('microsoft.netcore.app.runtime.'+actual['rid'])/'11.0.0'),'inputReceipt':receipt}


def validate_native_packs(selected,rid):
    """Compare NuGet extraction with the independently pinned signed archives.

    NuGet omits packaging relationships and lowercases its nuspec filename.
    The only extra files allowed are its original nupkg and three cache records.
    No directory is admitted using a manifest derived from that directory.
    """
    feed=Path(selected['privateFeed']);packages=Path(selected['privateEnvironment']['NUGET_PACKAGES']);proof=[]
    rows=selected['nativeRows'];required={r['id'] for r in rows}
    observed=set()
    for row in rows:
        lower=row['id'].lower();pack=packages/lower/row['version']
        if not pack.exists():
            require(row['id'] not in required,'OWN_REQUIRED_NATIVE_PACK_MISSING');continue
        require(pack.is_dir() and not pack.is_symlink(),'OWN_NATIVE_PACK_DIRECTORY');observed.add(row['id'])
        original=feed/(lower+'.'+row['version']+'.nupkg');require(hashlib.sha256(original.read_bytes()).hexdigest()==row['sha256'],'OWN_ORIGINAL_NATIVE_ARCHIVE_CHANGED')
        expected={}
        with zipfile.ZipFile(original) as archive:
            for info in archive.infolist():
                name=info.filename
                if info.is_dir() or name.startswith(('_rels/','package/')) or name=='[Content_Types].xml':continue
                target=name.lower() if name.endswith('.nuspec') else name
                require(target not in expected,'OWN_NATIVE_PROJECTION_COLLISION')
                expected[target]={'bytes':info.file_size,'sha256':hashlib.sha256(archive.read(info)).hexdigest()}
        archive_name=lower+'.'+row['version']+'.nupkg';extras={archive_name,archive_name+'.sha512','.nupkg.metadata'}
        actual={p.relative_to(pack).as_posix() for p in pack.rglob('*') if p.is_file()}
        require(actual==set(expected)|extras and not any(p.is_symlink() for p in pack.rglob('*')),'OWN_NATIVE_CACHE_NAMESPACE')
        for name,identity in expected.items():
            path=pack/name;require(path.stat().st_size==identity['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest()==identity['sha256'],'OWN_NATIVE_CACHE_MEMBER')
        require(hashlib.sha256((pack/archive_name).read_bytes()).hexdigest()==row['sha256'],'OWN_NATIVE_CACHED_RAW_ARCHIVE')
        require((pack/(archive_name+'.sha512')).read_text(encoding='utf-8-sig').strip()==row['contentHash'],'OWN_NATIVE_CACHE_NORMALIZED_HASH')
        metadata=json.loads((pack/'.nupkg.metadata').read_bytes());require(set(metadata)=={'version','contentHash','source'} and metadata['version']==2 and metadata['contentHash']==row['contentHash'] and Path(metadata['source']).resolve()==feed.resolve(),'OWN_NATIVE_MATERIALIZATION_METADATA')
        proof.append({'id':row['id'],'version':row['version'],'originalSignedArchiveSHA256':row['sha256'],'normalizedNuGetContentHash':row['contentHash'],'archiveMembersVerified':len(expected),'cacheNamespaceVerified':len(actual),'noticesVerifiedFromOriginalSignedArchive':True})
    require(required<=observed,'OWN_REQUIRED_NATIVE_PACKS');return proof
