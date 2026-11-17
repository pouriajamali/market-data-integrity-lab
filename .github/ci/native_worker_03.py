"""Inert fixed project03 CI product plan; no original semantic oracle edits."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import stat
import tarfile
import zipfile
from typed_host_03 import require


def run_fixed_plan(repo,own,binding,selected,actual):
    # Initial current source locks remain net10.0. Refuse full product route
    # until a separately admitted one-time net11 transition is actually recorded.
    for name in ['src/Integrity/packages.lock.json','tests/Integrity.Tests/packages.lock.json','tests/RecoveryProbe/packages.lock.json','tools/Measurements/packages.lock.json']:
        require(set(json.loads((repo/name).read_text())['dependencies'])=={'net11.0'},'NET11_LOCK_TRANSITION_NOT_ADMITTED')
    sdk=Path(selected['dotnet']);python=Path(selected['python'])
    require(sdk.is_absolute() and python.is_absolute(),'ABSOLUTE_SELECTED_TOOLS')
    rid=actual['rid'];out=repo/'artifacts/binary';pub=repo/'artifacts/publish'/rid
    env={k:v for k,v in os.environ.items() if k in ['SystemRoot','WINDIR','COMSPEC','LANG','LC_ALL']}
    env.update(selected['privateEnvironment'])
    env.update(DOTNET_ROOT=str(sdk.parent),DOTNET_HOST_PATH=str(sdk),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_CLI_USE_MSBUILD_SERVER='0',MSBUILDDISABLENODEREUSE='1',DOTNET_SYSTEM_IO_DISABLEFILELOCKING='0',MSBuildEnableWorkloadResolver='false')
    deadline=min(time.monotonic()+700,actual['deadlineMonotonicNs']/10**9);commands=[]
    def invoke(label,argv,expected=0):
        require(time.monotonic()<deadline,'NATIVE_PRODUCT_BUDGET')
        stdout=own/(label+'.stdout');stderr=own/(label+'.stderr')
        with stdout.open('xb') as so,stderr.open('xb') as se:
            child=subprocess.Popen([str(v) for v in argv],cwd=repo,env=env,stdout=so,stderr=se)
            while child.poll() is None:
                if time.monotonic()>=deadline or sum(p.stat().st_size for p in own.iterdir() if p.is_file() and p.suffix in ('.stdout','.stderr','.log'))>268435456:
                    # Outer owning Job/unit owns whole-tree kill. Do not fall
                    # back to process-name termination or claim completion.
                    raise RuntimeError('OWNING_CONTROLLER_MUST_TERMINATE_PRODUCT')
                time.sleep(0.05)
        require(child.returncode==expected,'PRODUCT_EXIT_MISMATCH')
        commands.append({'label':label,'argv':[str(v) for v in argv],'exit':child.returncode,'stdout':str(stdout),'stderr':str(stderr)})
        return stdout
    tests='tests/Integrity.Tests/Integrity.Tests.csproj';measure='tools/Measurements/Measurements.csproj';app='src/Integrity/Integrity.csproj'
    invoke('version',[sdk,'--version'])
    require((own/'version.stdout').read_text().strip()=='11.0.100','SELECTED_SDK_VERSION')
    offline=['--configfile',selected['offlineConfig'],'--packages',env['NUGET_PACKAGES'],'-p:NuGetAudit=false']
    invoke('restore-tests',[sdk,'restore',tests,'--locked-mode',*offline])
    invoke('build-tests',[sdk,'build',tests,'-c','Release','--no-restore','-m:1','-p:UseSharedCompilation=false'])
    units=invoke('units',[sdk,'run','--project',tests,'-c','Release','--no-build'])
    # Exact summary syntax must be source-reviewed against actual runner output;
    # no unparsed line is silently turned into165 passed tests.
    text=units.read_text(encoding='utf-8',errors='strict')
    require(re.search(r'Total:\s*165\b',text) and re.search(r'Failed:\s*0\b',text) and re.search(r'Skipped:\s*0\b',text),'UNIT_COUNTS_NOT_PROVED')
    invoke('restore-measurements',[sdk,'restore',measure,'--locked-mode',*offline])
    invoke('build-measurements',[sdk,'build',measure,'-c','Release','--no-restore','-m:1','-p:UseSharedCompilation=false'])
    invoke('measurement-oracles',[python,'-E','tools/test_measurements.py'])
    invoke('frozen-summary-recompute',[python,'-E','tools/analyze_measurements.py','docs/evidence/measurements','artifacts/recomputed-a4-summary.json'])
    invoke('tiny-worker-correctness',[python,'-E','tools/check_measurements.py','--dotnet',sdk])
    invoke('generate-a1',[sdk,'run','--project',app,'-c','Release','--no-build','--','generate','artifacts/demo','34'])
    invoke('validate-a1',[sdk,'run','--project',app,'-c','Release','--no-build','--','validate','artifacts/demo/manifest.json','artifacts/demo/input.jsonl','artifacts/result'],2)
    invoke('generate-a2',[sdk,'run','--project',app,'-c','Release','--no-build','--','generate-sequence','artifacts/sequence-demo'])
    invoke('validate-a2',[sdk,'run','--project',app,'-c','Release','--no-build','--','validate-sequence','artifacts/sequence-demo/manifest.json','artifacts/sequence-demo/input.jsonl','artifacts/sequence-result'],2)
    lock=Path(selected['admittedRidLock'])
    require(lock.is_file(),'RID_LOCK_NOT_ADMITTED')
    invoke('locked-rid-restore',[sdk,'restore',app,'-r',rid,'--locked-mode','-p:SelfContained=true','-p:RuntimeFrameworkVersion=11.0.0','-p:NuGetLockFilePath='+str(lock),*offline])
    from curate_inputs_03 import validate_native_packs
    native_proof=validate_native_packs(selected,rid)
    (own/'native-pack-materialization.json').write_text(json.dumps(native_proof),encoding='utf-8')
    invoke('publish',[sdk,'publish',app,'-c','Release','-r',rid,'--no-restore','--self-contained','true','-m:1','-p:UseSharedCompilation=false','-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:PublishAot=false','-p:PublishReadyToRun=false','-p:DebugType=None','-p:DebugSymbols=false','-p:RuntimeFrameworkVersion=11.0.0','-p:NuGetLockFilePath='+str(lock),'-o',pub])
    invoke('package',[python,'-E','tools/package_binary.py','--publish',pub,'--output',out,'--rid',rid,'--runtime-pack',selected['runtimePack'],'--commit',actual['commit'],'--sdk','11.0.100'])
    # Strict extraction is bound to the producer-reviewed portable data-only
    # provider; no generic allow-links/tar fallback or arbitrary shell.
    extracted=extract_own(repo,own,rid)
    smoke=repo/('artifacts/binary-smoke-'+rid+'.json')
    invoke('apphost',[python,'-E','tools/check_binary.py','--package',extracted,'--work',repo/'artifacts/apphost cwd with spaces','--commit',actual['commit'],'--receipt',smoke])
    value=json.loads(smoke.read_text())
    require(value['commit']==actual['commit'] and value['rid']==rid and len(value['observations'])==60,'APPHOST60_NOT_PROVED')
    require(value['status']=='EXTRACTED_APPHOST_DEMO_AND_CONTRACT_PASS','APPHOST_CHECK_INCOMPLETE')
    return {'commands':commands,'nativePackMaterialization':native_proof,'unitsObserved':165,'apphostInvocationsObserved':60,'benchmarkExecuted':False,'otherRidRequiredForFinalDelivery':True}

def extract_own(repo,own,rid):
    prefix='market-data-integrity-lab-v0.6.2-preview.1-'+rid
    path=repo/'artifacts/binary'/(prefix+('.zip' if rid=='win-x64' else '.tar.gz'))
    require(path.is_file() and not path.is_symlink() and path.stat().st_size<=536870912,'OWN_ARCHIVE')
    target=own/'extracted path with spaces';require(not target.exists(),'FRESH_EXTRACTION')
    with (zipfile.ZipFile(path) if rid=='win-x64' else tarfile.open(path,'r:gz')) as arc:
        rows=arc.infolist() if rid=='win-x64' else arc.getmembers();require(len(rows)<=4096,'MEMBERS')
        seen=set();total=0
        for row in rows:
            name=row.filename if rid=='win-x64' else row.name;size=row.file_size if rid=='win-x64' else row.size
            require(name and '\\' not in name and ':' not in name and not name.startswith('/') and '..' not in name.split('/') and name.split('/')[0]==prefix and name not in seen,'PATH')
            seen.add(name);require(not stat.S_ISLNK(row.external_attr>>16) if rid=='win-x64' else row.isfile() or row.isdir(),'TYPE')
            total+=size;require(0<=size<=67108864 and total<=1073741824,'SIZE')
        target.mkdir()
        for row in rows:
            name=row.filename if rid=='win-x64' else row.name;dest=target/name
            if row.is_dir() if rid=='win-x64' else row.isdir():dest.mkdir(parents=True,exist_ok=True);continue
            dest.parent.mkdir(parents=True,exist_ok=True)
            with (arc.open(row) if rid=='win-x64' else arc.extractfile(row)) as inp,dest.open('xb') as out:
                count=0
                for block in iter(lambda:inp.read(65536),b''):
                    count+=len(block);require(count<=(row.file_size if rid=='win-x64' else row.size),'STREAM');out.write(block)
                require(count==(row.file_size if rid=='win-x64' else row.size),'SHORT')
            if rid!='win-x64':dest.chmod(row.mode&0o777)
    return target/prefix
