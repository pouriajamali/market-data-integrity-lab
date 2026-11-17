"""Inert CI-only project03 controller adapter proposal, not runtime-admitted.

The shared04 transport is source-pinned and remains inert,
not a fabricated executable/qualification result. No fallback SDK is permitted.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import secrets
import select
import shlex
import shutil
import datetime
import urllib.request
import signal
import subprocess
import sys
import time
from typed_host_03 import Denied, require, verify_fact_bytes, check, PROFILE, bind_current_execution

JOB_SECONDS=1200
WORK_SECONDS=1110
MAX_JSON=8*1024*1024


def remaining(actual,ceiling=WORK_SECONDS):
    seconds=(actual['deadlineMonotonicNs']-time.monotonic_ns())/10**9
    require(seconds>0,'ORIGINAL_DEADLINE_EXHAUSTED')
    return min(ceiling,seconds)


def digest(path, maximum=MAX_JSON):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size<=maximum,'SOURCE_FILE_BOUND')
    raw=path.read_bytes();require(len(raw)<=maximum,'SOURCE_FILE_CHANGED')
    return hashlib.sha256(raw).hexdigest()


def document(path, expected):
    require(digest(path)==expected,'BINDING_DIGEST')
    return json.loads(path.read_text(encoding='utf-8'))


def observe(binding):
    require(os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted','NOT_GITHUB_HOSTED')
    require(os.environ.get('GITHUB_REPOSITORY')=='pouriajamali/market-data-integrity-lab','NOT_PROJECT03')
    require(os.environ.get('RUNNER_ARCH')=='X64' and platform.machine().lower() in ('amd64','x86_64'),'NOT_X64')
    repo=Path(os.environ['GITHUB_WORKSPACE']).resolve(strict=True)
    temp=Path(os.environ['RUNNER_TEMP']).resolve(strict=True)
    require(repo.is_dir() and temp.is_dir() and not repo.is_symlink() and not temp.is_symlink(),'RUNNER_ROOTS')
    actual={'repository':os.environ['GITHUB_REPOSITORY'],'runId':int(os.environ['GITHUB_RUN_ID']),
            'runAttempt':int(os.environ['GITHUB_RUN_ATTEMPT']),'jobKey':os.environ['GITHUB_JOB'],
            'runnerName':os.environ['RUNNER_NAME'],'commit':os.environ['GITHUB_SHA'],
            'runnerEnvironment':os.environ['RUNNER_ENVIRONMENT'],'imageOS':os.environ['ImageOS'],
            'imageVersion':os.environ['ImageVersion'],'container':Path('/.dockerenv').exists(),
            'selfHosted':False,'rid':'win-x64' if os.name=='nt' else 'linux-x64'}
    if os.name=='nt':
        info=sys.getwindowsversion();actual.update(hostClass='GitHubHostedWindowsServer2025x64',windowsProductType=info.product_type,windowsBuild=info.build)
        require(info.product_type in (2,3) and info.build==26100,'LOCAL_OR_OTHER_WINDOWS_EXCLUDED')
    else:
        release=dict(line.split('=',1) for line in Path('/etc/os-release').read_text().splitlines() if '=' in line)
        actual.update(hostClass='GitHubHostedUbuntu24.04x64',osReleaseId=release.get('ID','').strip('"'),osReleaseVersion=release.get('VERSION_ID','').strip('"'),uid=os.getuid(),gid=os.getgid())
        require(actual['osReleaseId']=='ubuntu' and actual['osReleaseVersion']=='24.04' and os.getuid()>0 and os.getgid()>0,'LOCAL_OR_ROOT_LINUX_EXCLUDED')
    # Actual numeric jobID/source tree come from the pinned read-only GitHub job
    # identity input inside the controlled worker, never from producer jobID.
    require(binding['status']=='AUTHORIZED_OWN_SOURCE_CURRENT_HOST_PROOF_REQUIRED','INERT_DESIGN_NOT_EXECUTABLE')
    require(tuple(sys.version_info[:3])==(3,12,10) if os.name=='nt' else tuple(sys.version_info[:2])==(3,12),'PINNED_EXISTING_PYTHON_REQUIRED')
    for item in binding['sourceFiles']:
        path=(repo/item['path']).resolve(strict=True)
        require(path.is_relative_to(repo) and digest(path,2*1024*1024)==item['sha256'],'OWN_SOURCE_MISMATCH')
    require(digest(Path(sys.executable),32*1024*1024)==binding['controllerPython'][actual['rid']]['sha256'],'CONTROLLER_INTERPRETER')
    token=os.environ.get('GH_TOKEN');require(token,'READ_ONLY_JOB_IDENTITY_REQUIRED')
    url='https://api.github.com/repos/'+actual['repository']+'/actions/runs/'+str(actual['runId'])+'/attempts/'+str(actual['runAttempt'])+'/jobs?per_page=20'
    request=urllib.request.Request(url,headers={'Authorization':'Bearer '+token,'Accept':'application/vnd.github+json','User-Agent':'Integrity03-current-job'})
    with urllib.request.urlopen(request,timeout=10) as response:
        raw=response.read(1048577);require(len(raw)<=1048576,'JOB_IDENTITY_BOUND')
    jobs=json.loads(raw)['jobs'];matches=[j for j in jobs if j['runner_name']==actual['runnerName'] and j['head_sha']==actual['commit'] and j['status']=='in_progress']
    require(len(matches)==1,'OWN_CURRENT_API_JOB');job=matches[0]
    actual.update(jobId=job['id'],jobStartedAt=job['started_at'])
    return repo,temp,actual


def linux_starter(repo,temp,actual,binding,pinned_binding):
    """One existing root OS starter; no root SDK, shell, install or repair."""
    actual.update(supervisorPID=os.getpid(),supervisorStartIdentity='proc-start:'+Path('/proc/self/stat').read_text().rsplit(')',1)[1].split()[19])
    unit='integrity03-'+str(actual['runId'])+'-'+str(actual['runAttempt'])+'-'+secrets.token_hex(6)+'-linux-x64'
    require(unit.replace('-','').isalnum() and len(unit)<=100,'UNIT_NAME')
    for tool in ('/usr/bin/sudo','/usr/bin/systemd-run','/usr/bin/systemctl'):
        require(Path(tool).is_file() and digest(Path(tool),32*1024*1024)==binding['trustedTools'][tool],'STARTER_TOOL_IDENTITY')
    own=temp/('integrity03-'+secrets.token_hex(12));own.mkdir(mode=0o700)
    require(own.stat().st_uid==actual['uid'] and own.stat().st_mode&0o777==0o700,'OWN_TEMP_OWNER_MODE')
    context=own/'context.json'
    # Context contains only pinned metadata, paths and redacted env. Credentials
    # are not serialized here; provider must use the explicitly reviewed separate
    #0600 runner credential transport, never argv/log/query-string credentials.
    payload={'repo':str(repo),'temp':str(own),'actual':actual,'bindingPath':str(pinned_binding),'bindingSha256':digest(pinned_binding),'unit':unit}
    context.write_text(json.dumps(payload),encoding='utf-8');context.chmod(0o600)
    require(remaining(actual)>20,'NO_START_STOP_RESERVE')
    observer=shlex.join([str(Path(sys.executable).resolve()),'-E',str(repo/'.github/ci/host_controller03.py'),'--stop-observer',str(context)])
    require(not any(c in observer for c in ('\n','\r','%','$')),'FIXED_OBSERVER_ARGV')
    argv=['/usr/bin/sudo' ,'-n','/usr/bin/systemd-run','--unit='+unit,'--no-block','--service-type=exec',
          '--property=User='+str(actual['uid']),'--property=Group='+str(actual['gid']),
          '--property=ExecStopPost='+observer,'--property=SendSIGKILL=yes','--property=FinalKillSignal=SIGKILL','--property=MemoryMax=4294967296','--property=MemorySwapMax=0','--property=TasksMax=128',
          '--property=CPUQuota=200%','--property=CPUQuotaPeriodSec=100ms',
          '--property=RuntimeMaxSec='+str(remaining(actual,WORK_SECONDS)-20),'--property=TimeoutStopSec=5','--property=KillMode=control-group',
          '--property=Environment='+ ' '.join(k+'='+os.environ[k] for k in ('GITHUB_SHA','GITHUB_ACTIONS','RUNNER_ARCH','GITHUB_WORKSPACE','RUNNER_TEMP','GITHUB_REPOSITORY','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_JOB','RUNNER_NAME','RUNNER_ENVIRONMENT','RUNNER_OS','ImageOS','ImageVersion') if k in os.environ),'--property=WorkingDirectory='+str(repo),'--property=StandardOutput=append:'+str(own/'worker.log'),
          '--property=StandardError=append:'+str(own/'worker-error.log'),
          str(Path(sys.executable).resolve()),'-E',str(repo/'.github/ci/host_controller03.py'),'--held-worker',str(context)]
    subprocess.run(argv,check=True,timeout=remaining(actual,15))
    # No --collect and no reset-failed/kill/stop unrelated service. If the unit is
    # unavailable before cause capture, terminal evidence is INCOMPLETE, not PASS.
    shown=None;held=None
    while time.monotonic_ns()<min(actual['deadlineMonotonicNs'],actual['rowStartedMonotonicNs']+30*10**9):
        raw=subprocess.check_output(['/usr/bin/systemctl','show',unit,'--property=MainPID,ControlPID,ControlGroup,ActiveState,SubState,Result,ExecMainCode,ExecMainStatus'],timeout=remaining(actual,5),text=True)
        shown=dict(line.split('=',1) for line in raw.splitlines() if '=' in line)
        if (own/'held.json').is_file() and int(shown.get('MainPID','0'))>0:
            held=json.loads((own/'held.json').read_text());break
        time.sleep(remaining(actual,0.1))
    require(held is not None and held['pid']==int(shown['MainPID']) and held['uid']==actual['uid'] and held['gid']==actual['gid'],'OWN_HELD_CHILD_REQUIRED')
    pid=held['pid'];cg=Path('/sys/fs/cgroup')/shown['ControlGroup'].lstrip('/')
    require(cg.resolve().is_relative_to(Path('/sys/fs/cgroup')),'CGROUP_PATH')
    observed={name:(cg/name).read_text().strip() for name in ['memory.max','memory.swap.max','pids.max','cpu.max']}
    require(observed=={'memory.max':'4294967296','memory.swap.max':'0','pids.max':'128','cpu.max':'200000 100000'},'OWN_EFFECTIVE_KERNEL_LIMITS')
    status=Path('/proc/'+str(pid)+'/status').read_text()
    held_end=min(actual['deadlineMonotonicNs'],time.monotonic_ns()+2*10**9)
    while '\nState:\tT' not in status and time.monotonic_ns()<held_end:
        time.sleep(.01);status=Path('/proc/'+str(pid)+'/status').read_text()
    require('\nState:\tT' in status and str(pid) in (cg/'cgroup.procs').read_text().splitlines(),'HELD_MEMBERSHIP')
    require(Path('/proc/'+str(pid)+'/stat').read_text().rsplit(')',1)[1].split()[19]==held['start'],'HELD_START_IDENTITY')
    pidfd=os.pidfd_open(pid,0)  # retained identity handle, closed after owned terminal
    try:
        git=lambda *args: subprocess.check_output(['/usr/bin/git',*args],cwd=repo,timeout=remaining(actual,5),text=True).strip()
        actual['sourceTree']=git('rev-parse','HEAD^{tree}')
        require(git('rev-parse','HEAD')==actual['commit'],'HEAD_MISMATCH')
        profile_path=repo/'.github/ci/integrity03-profile.json'
        proof={'schema':'sdk11-own-current-cap-evidence/v1','projectId':'03','repository':actual['repository'],'sourceCommit':actual['commit'],'sourceTree':actual['sourceTree'],'profileSha256':digest(profile_path),'controllerSha256':digest(repo/'.github/ci/host_controller03.py'),
          'worker':{'pid':pid,'processStartIdentity':'proc-start:'+held['start'],'uid':actual['uid'],'gid':actual['gid'],'cgroup':shown['ControlGroup'],'kernelLimits':observed},
          'beforeExec':{'pid':pid,'processStartIdentity':'proc-start:'+held['start'],'cgroup':shown['ControlGroup'],'kernelLimits':observed,'state':'T','membershipBeforeRelease':True,'retainedStartHandle':True},
          'supervisor':{'pid':actual['supervisorPID'],'processStartIdentity':actual['supervisorStartIdentity'],'deadlineMonotonicNs':actual['deadlineMonotonicNs']},
          'rowStartedMonotonicNs':actual['rowStartedMonotonicNs'],'deadlineMonotonicNs':actual['deadlineMonotonicNs'],
          'bootstrap':{'pythonVersion':platform.python_version(),'pythonExeSha256':digest(Path(sys.executable).resolve(),32*1024*1024),'gitSha256':digest(Path('/usr/bin/git'),32*1024*1024),'systemctlSha256':digest(Path('/usr/bin/systemctl'),32*1024*1024)}}
        (own/'control-proof.json').write_text(json.dumps(proof,sort_keys=True,separators=(',',':')),encoding='utf-8')
        os.kill(pid,signal.SIGCONT)
        # RuntimeMaxSec owns termination. Only read/query this unit. No additional
        # arbitrary sudo/root command is introduced for stop/collect/host repair.
        observer_fd=None;stop=None
        while time.monotonic_ns()<actual['deadlineMonotonicNs']:
            if (own/'stop-held.json').is_file():
                from runtime_policy_03 import canonical
                stop=json.loads((own/'stop-held.json').read_bytes());opid=stop['pid']
                observer_fd=os.pidfd_open(opid,0)
                require(stop['unit']==unit and stop['remainingPopulationExcludingObserver']==0 and stop['limits']==observed and stop['cgroup']==shown['ControlGroup'],'STOP_OBSERVER_BINDING')
                statrow=Path('/proc',str(opid),'stat').read_text().rsplit(')',1)[1].split()
                require(statrow[19]==stop['start'] and statrow[0]=='T' and not select.select([observer_fd],[],[],0)[0],'STOP_OBSERVER_IDENTITY')
                require((cg/'cgroup.procs').read_text().split()==[str(opid)],'STOP_OBSERVER_EXCLUSIVE')
                signal.pidfd_send_signal(observer_fd,signal.SIGCONT)
                break
            for log in [own/'worker.log',own/'worker-error.log']:
                require(not log.exists() or log.stat().st_size<=134217728,'CI_LOG_CAP')
            time.sleep(remaining(actual,0.05))
        require(observer_fd is not None,'STOP_OBSERVER_MISSING')
        try:
            while time.monotonic_ns()<actual['deadlineMonotonicNs']:
                if select.select([pidfd],[],[],0)[0] and select.select([observer_fd],[],[],0)[0]:break
                time.sleep(remaining(actual,.02))
            require(select.select([pidfd],[],[],0)[0] and select.select([observer_fd],[],[],0)[0],'RETAINED_HANDLES_NOT_TERMINAL')
        finally:os.close(observer_fd)
        raw=subprocess.check_output(['/usr/bin/systemctl','show',unit,'--property=MainPID,ControlPID,Result,ExecMainCode,ExecMainStatus'],timeout=remaining(actual,5),text=True)
        final=dict(line.split('=',1) for line in raw.splitlines() if '=' in line)
        while (final.get('MainPID')!='0' or final.get('ControlPID')!='0') and time.monotonic_ns()<actual['deadlineMonotonicNs']:
            time.sleep(.02)
            raw=subprocess.check_output(['/usr/bin/systemctl','show',unit,'--property=MainPID,ControlPID,Result,ExecMainCode,ExecMainStatus'],timeout=remaining(actual,5),text=True)
            final=dict(line.split('=',1) for line in raw.splitlines() if '=' in line)
        require(final.get('MainPID')=='0' and final.get('ControlPID')=='0' and 'Result' in final,'CAUSE_OR_TERMINAL_UNKNOWN')
        require((not cg.exists()) or ((cg/'cgroup.events').is_file() and 'populated 0' in (cg/'cgroup.events').read_text()),'POPULATION_ZERO_NOT_OBSERVED')
        terminal={'unit':unit,'terminal':final,'populationZeroObserved':True,'retainedWorkerAndObserverTerminal':True,'stopObserver':stop}
        (own/'controller-terminal.json').write_text(json.dumps(terminal),encoding='utf-8')
        retain(repo,own)
        require(final.get('Result')=='success' and final.get('ExecMainStatus')=='0' and (own/'worker-result.json').is_file(),'OWN_WORKER_NOT_SUCCESS')
        result={'unit':unit,'starterArgv':argv,'workerBoundary':'actual nonroot runner','controls':proof,'terminal':final,'populationZeroObserved':True}
        (own/'controller-result.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
        return own
    finally:
        retain(repo,own)
        os.close(pidfd)


def controlled_worker(context):
    """Only fixed producer04 transport + fixed project03 native worker plan."""
    own=Path(context['temp']);repo=Path(context['repo'])
    binding=document(Path(context['bindingPath']),context['bindingSha256'])
    require(Path.cwd().resolve()==repo and os.getuid()==context['actual']['uid'] and os.getgid()==context['actual']['gid'],'ACTUAL_WORKER_IDENTITY')
    held={'pid':os.getpid(),'uid':os.getuid(),'gid':os.getgid(),'cwd':str(repo),'start':Path('/proc/self/stat').read_text().rsplit(')',1)[1].split()[19],'cgroup':Path('/proc/self/cgroup').read_text()}
    with (own/'held.json').open('x',encoding='utf-8') as f:json.dump(held,f)
    os.kill(os.getpid(),signal.SIGSTOP)
    proof=json.loads((own/'control-proof.json').read_text())
    actual=context['actual'];actual.update(sourceTree=proof['sourceTree'],rowStartedMonotonicNs=proof['rowStartedMonotonicNs'],deadlineMonotonicNs=proof['deadlineMonotonicNs']);actual['ownControlProof']=proof
    worker_pipeline(repo,own,binding,actual)


def worker_pipeline(repo,own,binding,actual):
    if binding['purpose']=='SDK_FREE_CONTROL_PROBE':
        # A separate exact probe admission. No SDK handoff is forged or loaded.
        require(actual['ownControlProof'].get('beforeExec',{}).get('membershipBeforeRelease',False),'PROBE_HELD_CHILD')
        with (own/'worker-result.json').open('x',encoding='utf-8') as f:
            json.dump({'scope':'OWN_CURRENT_CI_CONTROL_PROBE_ONLY','actual':actual,'SDKSpawns':0,'sdkReadinessEstablished':False},f)
        return
    require(binding['purpose']=='OWN_FIXED_INPUT_AND_PRODUCT','EXACT_PURPOSE_REQUIRED')
    from runtime_policy_03 import bind
    context=bind(repo,own,actual,actual['ownControlProof'])
    provider=repo/'tools/check_dotnet11.py'
    require(digest(provider)=='c37e2f48ee21ef1f02e4afb2bb1cd0af397781e487265c31e43fe06ea182befd','SHARED_SOURCE')
    spec=importlib.util.spec_from_file_location('producer04_bound_transport',provider)
    adapter=importlib.util.module_from_spec(spec);sys.modules[spec.name]=adapter;spec.loader.exec_module(adapter)
    sdk,receipt=adapter.transport(actual['rid'],Path(os.environ['RUNNER_TEMP'])/('sdk11-normal-03-'+secrets.token_hex(12)),context)
    (own/'shared-input-receipt.json').write_text(json.dumps(receipt),encoding='utf-8')
    adapter.inspect_sdk(actual['rid'],sdk,context)
    from curate_inputs_03 import curate
    catalog=json.loads((repo/'.github/ci'/('integrity03-'+actual['rid'].split('-')[0]+'-inputs.json')).read_bytes())
    selected=curate(repo,own,sdk,actual,catalog,actual['stage'])
    if actual['stage']=='input-curation':result={'scope':'OWN_ACTUAL_INPUT_AND_GRAPH_ONLY','productExecuted':False,'inputReceipt':selected['inputReceipt']}
    else:
        from native_worker_03 import run_fixed_plan
        result=run_fixed_plan(repo,own,binding,selected,actual)
    result['inputRecheck']=adapter.inspect_sdk(actual['rid'],sdk,context)
    (own/'worker-result.json').write_text(json.dumps(result),encoding='utf-8')


def retain(repo,own):
    target=repo/'artifacts/host-proof';target.mkdir(parents=True,exist_ok=True)
    rows=[p for p in own.iterdir() if p.is_file() and p.suffix in ('.json','.log','.stdout','.stderr')]
    require(sum(p.stat().st_size for p in rows)<=32*1024*1024,'OWN_PROOF_ARTIFACT_CAP')
    for path in rows:shutil.copyfile(path,target/path.name)


def route_main(WindowsJob,row_started):
    parser=argparse.ArgumentParser();parser.add_argument('--binding',required=True);parser.add_argument('--binding-sha256',required=True);parser.add_argument('--stage',choices=['input-curation','product'],default='product')
    args=parser.parse_args();binding=document(Path(args.binding),args.binding_sha256)
    repo,temp,actual=observe(binding)
    began=datetime.datetime.fromisoformat(actual['jobStartedAt'].replace('Z','+00:00')).timestamp()
    start=min(row_started,time.monotonic_ns()-int(max(0,time.time()-began)*10**9))
    actual.update(stage=args.stage,rowStartedMonotonicNs=start,deadlineMonotonicNs=start+WORK_SECONDS*10**9)
    remaining(actual)
    require(binding['profile']==PROFILE,'OWN_PROFILE_REQUIRED')
    require(digest(repo/'.github/ci/host_controller03.py')==binding['controllerSha256'],'CONTROLLER_SOURCE')
    if os.name!='nt':return linux_starter(repo,temp,actual,binding,Path(args.binding).resolve())
    require(actual['rid'] in binding['nativeInputRids'],'WINDOWS_NATIVE_SHARED_INPUT_PENDING')
    # The original project03 Job source is reused by source identity only.
    # Every consuming Windows job must obtain fresh held-child/readback/active0.
    job=WindowsJob(binding,secrets.token_hex(16))
    own=temp/('integrity03-'+secrets.token_hex(12));own.mkdir()
    proof={'projectId':'03','runId':actual['runId'],'jobId':binding['consumerInstance']['jobId'],'sourceSha256':binding['controllerSha256'],'heldChildBeforeTargets':False,'effectiveProfile':PROFILE}
    context={'temp':str(own),'repo':str(repo),'actual':actual,'binding':binding}
    payload=own/'windows-context.json';payload.write_text(json.dumps(context),encoding='utf-8')
    argv=[str(Path(sys.executable).resolve()),'-E',str(Path(__file__).resolve()),'--windows-worker',str(payload)]
    # spawn() assigns the suspended root and reads effective limits before resume.
    started=time.monotonic();child=job.spawn(argv,os.environ.copy())
    require(job.lastHeld['pid']==child.pid and job.lastHeld['resumePreviousCount']==1,'WINDOWS_HELD_CHILD_IDENTITY')
    proof['heldChildBeforeTargets']=True;proof['effectiveReadback']=job.controls();proof['heldChild']=job.lastHeld
    actual['ownControlProof']=proof
    raw_proof=json.dumps(proof,sort_keys=True,separators=(',',':')).encode()
    with (own/'kernel-readback.json').open('xb') as evidence:evidence.write(raw_proof)
    proof['evidenceSha256']=hashlib.sha256(raw_proof).hexdigest()
    (own/'control-proof.json').write_text(json.dumps(proof),encoding='utf-8')
    try:
        child.communicate(timeout=remaining(actual,WORK_SECONDS))
        require(child.returncode==0,'WINDOWS_WORKER_INCOMPLETE')
    finally:
        if job.active():job.stop()
        child.wait(timeout=remaining(actual,5))
        require(not job.active(),'WINDOWS_OWNED_DESCENDANTS_NOT_ZERO')
        terminal={'status':'OWN_JOB_TERMINAL_NOT_GLOBAL_READY','controls':job.controls(),'childExit':child.returncode,'durationSeconds':time.monotonic()-started}
        (own/'controller-result.json').write_text(json.dumps(terminal,indent=2),encoding='utf-8');job.close()
    return own


def entry(WindowsJob):
    row_started=time.monotonic_ns()
    try:
        if len(sys.argv)==3 and sys.argv[1]=='--stop-observer':
            from runtime_policy_03 import stop_observer
            stop_observer(json.loads(Path(sys.argv[2]).read_bytes()))
        elif len(sys.argv)==3 and sys.argv[1]=='--held-worker':
            controlled_worker(json.loads(Path(sys.argv[2]).read_text()))
        elif len(sys.argv)==3 and sys.argv[1]=='--windows-worker':
            c=json.loads(Path(sys.argv[2]).read_text());own=Path(c['temp'])
            deadline=min(c['actual']['deadlineMonotonicNs'],time.monotonic_ns()+5*10**9)
            while not (own/'control-proof.json').is_file() and time.monotonic_ns()<deadline:time.sleep(remaining(c['actual'],0.01))
            c['actual']['ownControlProof']=json.loads((own/'control-proof.json').read_text())
            worker_pipeline(Path(c['repo']),own,c['binding'],c['actual'])
        else:route_main(WindowsJob,row_started)
    except (Denied,OSError,ValueError,KeyError,TypeError,subprocess.SubprocessError) as error:
        print(json.dumps({'status':'HOLD_INCOMPLETE','code':str(error) if isinstance(error,Denied) else type(error).__name__}),file=sys.stderr);sys.exit(70)
