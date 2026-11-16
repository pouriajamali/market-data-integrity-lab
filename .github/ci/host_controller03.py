"""Private project03 native phase launcher source; NOT YET EXECUTED/ADMITTED.

Fixed Integrity recipes only. Windows entry is run-03-windows.ps1. The manager
must bind this source and actual native controls/SDK input before execution.
No arbitrary command, alternate project, shared bootstrap or cap override.
"""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import secrets
import signal
import stat
import subprocess
import sys
import tarfile
import threading
import time
import zipfile

BASE = '2679d98607bc043860ebf3a5a216797797b6ab16'
BRANCH = 'codex/integrity-net11-csharp15-20261114'
M0_SHA = 'a7ca6011a0e9b9a62aa51adf08b8cf9297d505f63a72ebb7363826547fa30a80'
APPROVAL_SHA = '142dac81ae7f6e05f52319d3521d4825d5df028e37542b7623d16316ff7dd077'
PROTOCOL_SHA = '9120ca49a0d46673053aa0ec0cdbbc6f00731d71ea0b57ddab09e8fc3ac3ac8b'
PROJECT = Path('E:/Codex WorkSpace/Git Projects/market-data-integrity-lab') if os.name == 'nt' else Path('/mnt/e/Codex WorkSpace/Git Projects/market-data-integrity-lab')
WORK = PROJECT/'tech-refresh-net11-20261114'
REPO = WORK/'repo'
STATE = WORK/'state'
HELPERS = STATE/'operator-helpers'
OUTPUTS = PROJECT/'outputs/tech-refresh-m0-20261114'
MANAGER = PROJECT.parent/'portfolio-management/development-management-20261110'
PROTOCOL = MANAGER/'wave2-state/LEASE-PROTOCOL.json'
LEASE = MANAGER/'wave2-state/LOCAL-FIXTURE-LEASE.json'
MEMORY = 4294967296
PIDS = 128
LOG_BYTES = 268435456
SCRATCH_BYTES = 8589934592
FREE_BYTES = 17179869184
PHASES = {'probe-normal':120,'probe-deadline':120,'sdk-identity':120,'locks':1800,'restore':1800,'build':1800,'units':1800,'correctness':1800,'policy':600,'runtime-locks':1800,'publish':1800,'package':1200,'smoke':1200,'audit':1800}
CURRENT_GROUP = None
CURRENT_ENV = None
TRUSTED_GIT = None


class Hold(Exception):
    pass


def need(ok, code):
    if not ok:
        raise Hold(code)


def sha(path, limit=8*1024*1024):
    need(path.is_file() and path.stat().st_size <= limit, 'HASH_INPUT_SIZE_OR_TYPE')
    h = hashlib.sha256()
    with path.open('rb') as stream:
        count = 0
        for block in iter(lambda: stream.read(65536), b''):
            count += len(block)
            need(count <= limit, 'HASH_INPUT_SIZE_CHANGED')
            h.update(block)
    return h.hexdigest()


def ordinary(path, root, directory=False):
    need(path.is_absolute() and path.resolve().is_relative_to(root.resolve()), 'PATH_ESCAPE')
    cursor = path
    while True:
        info = cursor.lstat()
        need(not stat.S_ISLNK(info.st_mode) and not getattr(info,'st_file_attributes',0)&0x400, 'LINK_OR_REPARSE')
        if cursor == root:
            break
        need(cursor != cursor.parent, 'ROOT_NOT_REACHED')
        cursor = cursor.parent
    need(path.is_dir() if directory else path.is_file(), 'PATH_TYPE')
    return path


def load(path, expected=None, limit=2*1024*1024):
    need(path.is_file() and path.stat().st_size <= limit, 'JSON_SIZE')
    raw = path.read_bytes()
    need(len(raw) <= limit, 'JSON_SIZE_CHANGED')
    if expected is not None:
        need(hashlib.sha256(raw).hexdigest() == expected, 'PIN_MISMATCH')
    def pairs(items):
        value = {}
        for key,item in items:
            need(key not in value, 'JSON_DUPLICATE_KEY')
            value[key] = item
        return value
    return json.loads(raw.decode('utf-8'), object_pairs_hook=pairs)


def emit(path, value):
    with path.open('x',encoding='utf-8',newline='\n') as out:
        json.dump(value,out,indent=2);out.write('\n');out.flush();os.fsync(out.fileno())


def process_start():
    if os.name != 'nt':
        return Path('/proc/self/stat').read_text().rsplit(')',1)[1].split()[19]
    creation=wintypes.FILETIME();end=wintypes.FILETIME();kernel=wintypes.FILETIME();user=wintypes.FILETIME()
    k=ctypes.WinDLL('kernel32',use_last_error=True)
    k.GetCurrentProcess.restype=wintypes.HANDLE
    k.GetProcessTimes.argtypes=[wintypes.HANDLE,*([ctypes.POINTER(wintypes.FILETIME)]*4)]
    need(k.GetProcessTimes(k.GetCurrentProcess(),ctypes.byref(creation),ctypes.byref(end),ctypes.byref(kernel),ctypes.byref(user)), 'PROCESS_START_IDENTITY')
    return str((creation.dwHighDateTime<<32)|creation.dwLowDateTime)


def git(*args):
    need(CURRENT_GROUP is not None,'GIT_REQUIRES_ADMITTED_KERNEL_GROUP')
    need(TRUSTED_GIT is not None,'PINNED_GIT_REQUIRED')
    proc=CURRENT_GROUP.spawn([str(TRUSTED_GIT),'--no-optional-locks','-c','core.fsmonitor=false','-C',str(REPO),*args],CURRENT_ENV)
    try:out,err=proc.communicate(timeout=30)
    except BaseException:CURRENT_GROUP.stop();proc.wait(timeout=10);raise
    need(not CURRENT_GROUP.active(),'GIT_DESCENDANTS_REMAIN')
    need(proc.returncode == 0 and len(out) <= 2*1024*1024 and len(err)<=65536, 'GIT_READ')
    return out.decode('utf-8').rstrip('\n')


def source_binding(a):
    m0=load(OUTPUTS/'REVIEW-PACKET.json',M0_SHA)
    base=load(OUTPUTS/'BASELINE.json',a['baselineSha256'])
    prep=load(OUTPUTS/'SOURCE-PREPARATION.json',a['sourcePreparationSha256'])
    need(git('branch','--show-current') == BRANCH, 'WRONG_BRANCH')
    git('merge-base','--is-ancestor',BASE,'HEAD')
    need(not (REPO/'.git/commondir').exists() and not (REPO/'.git/objects/info/alternates').exists(), 'COMMON_GIT')
    changed=set(git('diff','--name-only',BASE).splitlines())|set(git('ls-files','--others','--exclude-standard').splitlines())
    need(changed <= set(m0['allowedRepositoryPathsAfterApproval']), 'SOURCE_ALLOWLIST')
    # Also cover unchanged tracked files omitted from M0's small hash selection,
    # including the measurement worker. Only exact allowed paths may differ.
    docs={'README.md','CHANGELOG.md','NOTICE.md','PROVENANCE.md','docs/DEVELOPMENT.md','docs/RELEASE-v0.6.2-preview.1.md'}
    locks={p for p in m0['allowedRepositoryPathsAfterApproval'] if p.endswith('packages.lock.json')}
    initial={p['path']:p for p in prep['sourceFiles']}
    rows=[]
    for item in base['selectedFiles']:
        name=item['path'];actual=sha(REPO/name,2*1024*1024)
        if name not in docs|locks:
            need(actual == initial.get(name,item)['sha256'], 'CODE_OR_ORACLE_CHANGED')
        rows.append({'path':name,'sha256':actual})
    for name in locks:
        old=base['sourceLocks'][name]['content']['dependencies']
        new=load(REPO/name)['dependencies']
        need(set(new) in ({'net10.0'},{'net11.0'}), 'UNEXPECTED_SOURCE_LOCK_TARGETS')
        need(next(iter(new.values())) == next(iter(old.values())), 'SOURCE_PACKAGE_GRAPH_CHANGED')
    image=hashlib.sha256(json.dumps(rows,sort_keys=True,separators=(',',':')).encode()).hexdigest()
    return {'head':git('rev-parse','HEAD'),'branch':BRANCH,'selectedFiles':rows,'selectedImageSha256':image}


def admit(path, expected):
    global TRUSTED_GIT
    ordinary(path,OUTPUTS)
    a=load(path,expected)
    need(a['schema']=='integrity-03-native-admission/v1' and a['status']=='ADMITTED' and a['projectId']=='03','MANAGER_ADMISSION_REQUIRED')
    need(a['completeApprovalSha256']==APPROVAL_SHA and a['reviewedM0Sha256']==M0_SHA,'CONTRACT_BINDING')
    need(a['profile']=={'memoryBytes':MEMORY,'cpuEquivalents':2,'pids':PIDS,'linuxSwapBytes':0,'outerPhaseSeconds':1800,'logsBytes':LOG_BYTES,'monitoredScratchBytes':SCRATCH_BYTES},'PROFILE_WIDENED_OR_CHANGED')
    need(a['rid']==('win-x64' if os.name=='nt' else 'linux-x64') and Path(a['nativeCheckout'])==REPO,'NATIVE_CHECKOUT_OR_RID')
    need(Path.cwd().resolve()==REPO.resolve(),'NATIVE_CONTROLLER_CWD')
    need(platform.machine().lower() in ('x86_64','amd64'),'ARCHITECTURE')
    if os.name!='nt':
        need(os.getuid()==1000 and a['uid']==1000 and os.environ.get('WSL_DISTRO_NAME')=='Ubuntu-24.04','NATIVE_UID_OR_DISTRO')
        need(Path('/proc/sys/kernel/random/boot_id').read_text().strip()==a['nativeBootId'],'NATIVE_BOOT_BINDING_STALE')
    ordinary(REPO,PROJECT,directory=True)
    need(sha(PROTOCOL)==PROTOCOL_SHA and '03' in load(PROTOCOL)['participants'],'PROTOCOL_CHANGED')
    actual={p.name:sha(p) for p in HELPERS.iterdir() if p.is_file()}
    need(actual==a['launcherFiles'],'LAUNCHER_IDENTITY')
    need(Path(sys.executable).resolve()==Path(a['controllerPython']['path']).resolve(),'CONTROLLER_PATH')
    need(sha(Path(sys.executable),32*1024*1024)==a['controllerPython']['sha256'],'CONTROLLER_IDENTITY')
    # The externally pinned manager admission is the authority. A producer READY
    # string or self-hash is never accepted without that durable consumer binding.
    load(Path(a['sharedSdkHandoff']['path']),a['sharedSdkHandoff']['sha256'],8*1024*1024)
    load(Path(a['nativeCpuPrerequisiteReceipt']['path']),a['nativeCpuPrerequisiteReceipt']['sha256'])
    sdk=Path(a['sdk']['executable'])
    allowed=PROJECT.parent/'data-admission-gate/state/dotnet11-shared-20261114' if os.name=='nt' else Path('/home/ubuntu/admission04-dotnet11-20261116-r3')
    ordinary(sdk,allowed)
    need(sdk.name==('dotnet.exe' if os.name=='nt' else 'dotnet'),'SDK_ENTRY')
    need(sha(sdk,32*1024*1024)==a['sdk']['sha256'],'SDK_ENTRY_IDENTITY')
    need(a['sdk']['version']=='11.0.100' and a['sdk']['runtime']=='11.0.0','SDK_VERSION_BINDING')
    critical=a['sdk']['criticalFiles'];need(0<len(critical)<=8,'SDK_CRITICAL_SET_CAP')
    total=0;names=set()
    for item in critical:
        file=ordinary(Path(item['path']),sdk.parent);total+=file.stat().st_size
        need(total<=128*1024*1024,'SDK_CRITICAL_BYTES_CAP')
        need(sha(file,64*1024*1024)==item['sha256'],'SDK_CRITICAL_IDENTITY');names.add(file.name)
    need({'csc.dll','MSBuild.dll'}<=names,'COMPILER_BINDING_REQUIRED')
    # Consume producer-qualified Core/Host pack evidence, not a second shared
    # acquisition/audit. NuGet may read this admitted immutable fallback root;
    # all project writes remain in the private cache.
    ordinary(Path(a['sharedPackFallbackDirectory']),allowed,directory=True)
    ordinary(Path(a['runtimePackDirectory']),allowed,directory=True)
    load(Path(a['sharedPackEvidence']['path']),a['sharedPackEvidence']['sha256'],8*1024*1024)
    lint=Path(a['sharedActionlint']['executable']);need(lint.is_absolute(),'ACTIONLINT_ABSOLUTE')
    need(sha(lint,32*1024*1024)==a['sharedActionlint']['sha256'],'ACTIONLINT_IDENTITY')
    load(Path(a['sharedActionlint']['acceptedReceipt']),a['sharedActionlint']['acceptedReceiptSha256'],8*1024*1024)
    TRUSTED_GIT=Path(a['trustedGit']['path']);need(TRUSTED_GIT.is_absolute(),'TRUSTED_GIT_ABSOLUTE')
    need(sha(TRUSTED_GIT,64*1024*1024)==a['trustedGit']['sha256'],'TRUSTED_GIT_IDENTITY')
    return a,sdk


class LinuxGroup:
    def __init__(self,a,token):
        root=Path(a['delegatedCgroupParent'])
        need(root.is_absolute() and root.resolve().is_relative_to(Path('/sys/fs/cgroup')),'CGROUP_PARENT')
        need(root.is_dir() and not root.is_symlink(),'CGROUP_PARENT_TYPE')
        need(set(['cpu','memory','pids'])<=set((root/'cgroup.subtree_control').read_text().split()),'CONTROLLERS_NOT_DELEGATED')
        self.origin=Path('/sys/fs/cgroup')/Path('/proc/self/cgroup').read_text().split('0::',1)[1].strip().lstrip('/')
        need(self.origin.is_dir(),'CONTROLLER_ORIGIN_GROUP')
        self.aggregate=root/('integrity03-'+token)
        self.aggregate.mkdir()  # only new admitted subtree; never parent controls
        self.values={'memory.max':str(MEMORY),'memory.swap.max':'0','pids.max':str(PIDS),'cpu.max':'200000 100000'}
        for name,value in self.values.items():
            (self.aggregate/name).write_text(value)
            need((self.aggregate/name).read_text().strip()==value,'CGROUP_CONTROL_READBACK')
        (self.aggregate/'cgroup.subtree_control').write_text('+cpu +memory +pids')
        self.control=self.aggregate/'controller';self.control.mkdir()
        self.path=self.aggregate/'worker';self.path.mkdir()
        need((self.path/'cgroup.kill').is_file(),'CGROUP_KILL_REQUIRED')
        (self.control/'cgroup.procs').write_text(str(os.getpid()))
        need(str(os.getpid()) in (self.control/'cgroup.procs').read_text().splitlines(),'CONTROLLER_GROUP_MEMBERSHIP')
        self.fd=os.open(self.path/'cgroup.procs',os.O_WRONLY)
        self.expected='0::/'+str(self.path.relative_to('/sys/fs/cgroup'))
    def attach(self):
        os.write(self.fd,str(os.getpid()).encode())
        need(self.expected in Path('/proc/self/cgroup').read_text().splitlines(),'CHILD_NOT_IN_OWN_GROUP')
    def spawn(self,argv,env):
        return subprocess.Popen(argv,cwd=REPO,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,preexec_fn=self.attach)
    def stop(self): (self.path/'cgroup.kill').write_text('1')
    def active(self): return 'populated 1' in (self.path/'cgroup.events').read_text().splitlines()
    def controls(self):
        return {'aggregate':str(self.aggregate),'worker':str(self.path),'controller':str(self.control),'settings':{k:(self.aggregate/k).read_text().strip() for k in self.values},'workerMembers':(self.path/'cgroup.procs').read_text().splitlines(),'controllerMembers':(self.control/'cgroup.procs').read_text().splitlines(),'events':(self.aggregate/'memory.events').read_text()}
    def close(self):
        need(not self.active(),'OWNED_DESCENDANTS_REMAIN')
        os.close(self.fd)
        (self.origin/'cgroup.procs').write_text(str(os.getpid()))
        need(not (self.control/'cgroup.procs').read_text().strip(),'CONTROLLER_NOT_RETURNED')
        self.path.rmdir();self.control.rmdir();self.aggregate.rmdir()


class WindowsJob:
    def __init__(self,a,token):
        # Controller is assigned to an aggregate job before any target. A nested
        # worker job permits owned target-tree termination while controller lives.
        self.k=ctypes.WinDLL('kernel32',use_last_error=True)
        K=self.k
        class Basic(ctypes.Structure):
            _fields_=[('PerProcessUserTimeLimit',ctypes.c_longlong),('PerJobUserTimeLimit',ctypes.c_longlong),('LimitFlags',wintypes.DWORD),('MinimumWorkingSetSize',ctypes.c_size_t),('MaximumWorkingSetSize',ctypes.c_size_t),('ActiveProcessLimit',wintypes.DWORD),('Affinity',ctypes.c_size_t),('PriorityClass',wintypes.DWORD),('SchedulingClass',wintypes.DWORD)]
        class IO(ctypes.Structure):
            _fields_=[(n,ctypes.c_ulonglong) for n in ['ReadOperationCount','WriteOperationCount','OtherOperationCount','ReadTransferCount','WriteTransferCount','OtherTransferCount']]
        class Extended(ctypes.Structure):
            _fields_=[('BasicLimitInformation',Basic),('IoInfo',IO),('ProcessMemoryLimit',ctypes.c_size_t),('JobMemoryLimit',ctypes.c_size_t),('PeakProcessMemoryUsed',ctypes.c_size_t),('PeakJobMemoryUsed',ctypes.c_size_t)]
        class CPU(ctypes.Structure): _fields_=[('ControlFlags',wintypes.DWORD),('CpuRate',wintypes.DWORD)]
        class Accounting(ctypes.Structure):
            _fields_=[(n,ctypes.c_longlong) for n in ['TotalUserTime','TotalKernelTime','ThisPeriodTotalUserTime','ThisPeriodTotalKernelTime']]+[(n,wintypes.DWORD) for n in ['TotalPageFaultCount','TotalProcesses','ActiveProcesses','TotalTerminatedProcesses']]
        self.Extended=Extended;self.CPU=CPU;self.Accounting=Accounting
        K.CreateJobObjectW.argtypes=[ctypes.c_void_p,wintypes.LPCWSTR];K.CreateJobObjectW.restype=wintypes.HANDLE
        K.SetInformationJobObject.argtypes=[wintypes.HANDLE,ctypes.c_int,ctypes.c_void_p,wintypes.DWORD]
        K.QueryInformationJobObject.argtypes=[wintypes.HANDLE,ctypes.c_int,ctypes.c_void_p,wintypes.DWORD,ctypes.c_void_p]
        K.AssignProcessToJobObject.argtypes=[wintypes.HANDLE,wintypes.HANDLE]
        K.TerminateJobObject.argtypes=[wintypes.HANDLE,wintypes.UINT]
        K.CloseHandle.argtypes=[wintypes.HANDLE]
        K.GetCurrentProcess.restype=wintypes.HANDLE
        need((os.cpu_count() or 0)>0,'CPU_TOPOLOGY')
        self.rate=min(10000,math.floor(20000/os.cpu_count()))
        need(0<self.rate<=10000,'CPU_TOPOLOGY')
        def create(rate):
            job=K.CreateJobObjectW(None,None);need(job,'CREATE_JOB')
            ext=Extended();ext.BasicLimitInformation.LimitFlags=0x2000|0x200|0x8
            ext.BasicLimitInformation.ActiveProcessLimit=PIDS;ext.JobMemoryLimit=MEMORY
            cpu=CPU(1|4,rate)
            need(K.SetInformationJobObject(job,9,ctypes.byref(ext),ctypes.sizeof(ext)),'SET_JOB_LIMITS')
            need(K.SetInformationJobObject(job,15,ctypes.byref(cpu),ctypes.sizeof(cpu)),'SET_JOB_CPU')
            return job
        self.aggregate=create(self.rate)
        need(K.AssignProcessToJobObject(self.aggregate,K.GetCurrentProcess()),'CONTROLLER_JOB_ASSIGNMENT')
        # Nested CPU percentages are relative to the parent's allocation. Worker
        #100% preserves, rather than multiplies, the aggregate2-CPU ceiling.
        self.job=create(10000)
        self.lastPID=None
    def spawn(self,argv,env):
        process=subprocess.Popen(argv,cwd=REPO,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=0x4|0x08000000)
        try:
            need(self.k.AssignProcessToJobObject(self.job,wintypes.HANDLE(int(process._handle))),'SUSPENDED_CHILD_JOB_ASSIGNMENT')
            # Resume the single initial thread only after job assignment/readback.
            self.controls()
            class ThreadEntry(ctypes.Structure):
                _fields_=[('dwSize',wintypes.DWORD),('cntUsage',wintypes.DWORD),('th32ThreadID',wintypes.DWORD),('th32OwnerProcessID',wintypes.DWORD),('tpBasePri',wintypes.LONG),('tpDeltaPri',wintypes.LONG),('dwFlags',wintypes.DWORD)]
            K=self.k;K.CreateToolhelp32Snapshot.argtypes=[wintypes.DWORD,wintypes.DWORD];K.CreateToolhelp32Snapshot.restype=wintypes.HANDLE
            K.Thread32First.argtypes=[wintypes.HANDLE,ctypes.POINTER(ThreadEntry)];K.Thread32Next.argtypes=K.Thread32First.argtypes
            K.OpenThread.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];K.OpenThread.restype=wintypes.HANDLE
            K.ResumeThread.argtypes=[wintypes.HANDLE];K.ResumeThread.restype=wintypes.DWORD
            snap=K.CreateToolhelp32Snapshot(4,0);need(snap and snap!=ctypes.c_void_p(-1).value,'THREAD_SNAPSHOT')
            ids=[]
            try:
                entry=ThreadEntry();entry.dwSize=ctypes.sizeof(entry);ok=K.Thread32First(snap,ctypes.byref(entry))
                while ok:
                    if entry.th32OwnerProcessID==process.pid:ids.append(entry.th32ThreadID)
                    ok=K.Thread32Next(snap,ctypes.byref(entry))
            finally:K.CloseHandle(snap)
            need(len(ids)==1,'INITIAL_THREAD_IDENTITY')
            thread=K.OpenThread(2,False,ids[0]);need(thread,'OPEN_OWNED_THREAD')
            try:
                creation=wintypes.FILETIME();end=wintypes.FILETIME();kernel=wintypes.FILETIME();user=wintypes.FILETIME()
                K.GetProcessTimes.argtypes=[wintypes.HANDLE,*([ctypes.POINTER(wintypes.FILETIME)]*4)]
                need(K.GetProcessTimes(wintypes.HANDLE(int(process._handle)),ctypes.byref(creation),ctypes.byref(end),ctypes.byref(kernel),ctypes.byref(user)),'HELD_PROCESS_START')
                self.lastHeld={'pid':process.pid,'creationFileTime':str((creation.dwHighDateTime<<32)|creation.dwLowDateTime),'effectiveBeforeResume':self.controls()}
                previous=K.ResumeThread(thread);self.lastHeld['resumePreviousCount']=previous
                need(previous==1,'RESUME_OWNED_THREAD')
            finally:K.CloseHandle(thread)
            self.lastPID=process.pid
            return process
        except BaseException:
            self.k.TerminateJobObject(self.job,70);process.kill();process.wait(timeout=5);raise
    def stop(self): need(self.k.TerminateJobObject(self.job,124),'TERMINATE_OWN_JOB')
    def active(self):
        info=self.Accounting();need(self.k.QueryInformationJobObject(self.job,1,ctypes.byref(info),ctypes.sizeof(info),None),'JOB_ACCOUNTING')
        return info.ActiveProcesses!=0
    def controls(self):
        records=[]
        for job,expectedRate in [(self.aggregate,self.rate),(self.job,10000)]:
            ext=self.Extended();cpu=self.CPU()
            need(self.k.QueryInformationJobObject(job,9,ctypes.byref(ext),ctypes.sizeof(ext),None),'JOB_LIMIT_READBACK')
            need(self.k.QueryInformationJobObject(job,15,ctypes.byref(cpu),ctypes.sizeof(cpu),None),'JOB_CPU_READBACK')
            need(ext.JobMemoryLimit==MEMORY and ext.BasicLimitInformation.ActiveProcessLimit==PIDS and ext.BasicLimitInformation.LimitFlags==0x2208,'JOB_LIMIT_MISMATCH')
            need(cpu.ControlFlags==5 and cpu.CpuRate==expectedRate,'JOB_CPU_MISMATCH')
            records.append({'memoryBytes':ext.JobMemoryLimit,'pids':ext.BasicLimitInformation.ActiveProcessLimit,'cpuRate':cpu.CpuRate,'flags':ext.BasicLimitInformation.LimitFlags})
        return {'aggregateAndWorkerJobs':records,'workerPID':self.lastPID,'workerActive':self.active()}
    def close(self):
        need(not self.active(),'OWNED_DESCENDANTS_REMAIN')
        self.k.CloseHandle(self.job)
        # aggregate KILL_ON_JOB_CLOSE is intentionally retained to process exit;
        # closing it here would kill this controller before durable receipt exit.


def recipes(phase,sdk,rid,env,a):
    d=str(sdk);p=sys.executable
    tests='tests/Integrity.Tests/Integrity.Tests.csproj';measure='tools/Measurements/Measurements.csproj';app='src/Integrity/Integrity.csproj'
    runtime_lock=str(STATE/'runtime-locks'/f'{rid}.lock.json')
    publish=str(REPO/'artifacts/tech-refresh'/rid/'publish')
    binaries=str(REPO/'artifacts/tech-refresh'/rid/'binary')
    prefix=f'market-data-integrity-lab-v0.6.2-preview.1-{rid}'
    extracted=str(REPO/'artifacts/tech-refresh'/rid/'extracted package with spaces'/prefix)
    self_file=str(Path(__file__).resolve())
    audit=['-p:NuGetAudit=true','-p:NuGetAuditMode=all','-p:NuGetAuditLevel=low',f"-p:RestoreFallbackFolders={a['sharedPackFallbackDirectory']}"]
    base=[f'-p:RuntimeFrameworkVersion=11.0.0','-p:SelfContained=true',f'-p:NuGetLockFilePath={runtime_lock}',*audit]
    if phase.startswith('probe-'):
        return [([p,'-I',str(Path(__file__).resolve()),'--internal-probe','deadline' if phase=='probe-deadline' else 'normal'],2 if phase=='probe-deadline' else 30)]
    commands={
      'sdk-identity':[[d,'--version']],
      'locks':[[d,'restore',tests,'--force-evaluate',*audit],[d,'restore',measure,'--force-evaluate',*audit]],
      'restore':[[d,'restore',tests,'--locked-mode',*audit],[d,'restore',measure,'--locked-mode',*audit]],
      'build':[[d,'build',tests,'-c','Release','--no-restore','-m:1','-p:UseSharedCompilation=false'],[d,'build',measure,'-c','Release','--no-restore','-m:1','-p:UseSharedCompilation=false']],
      'units':[[d,'run','--project',tests,'-c','Release','--no-build']],
      'correctness':[[p,'-E','tools/test_measurements.py'],[p,'-E','tools/analyze_measurements.py','docs/evidence/measurements',f'artifacts/tech-refresh/{rid}/recomputed-summary.json'],[p,'-E','tools/check_measurements.py','--dotnet',d]],
      'policy':[[p,'-E','-m','unittest','discover','-s','.github/ci','-p','test_policy.py','-v'],[a['sharedActionlint']['executable'],'-color','.github/workflows/verify.yml','.github/workflows/csv-action.yml']],
      'runtime-locks':[[d,'restore',app,'-r',rid,'--force-evaluate',*base],[d,'restore',app,'-r',rid,'--locked-mode',*base]],
      'publish':[[d,'publish',app,'-c','Release','-r',rid,'--no-restore','--self-contained','true','-m:1','-p:UseSharedCompilation=false','-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:PublishAot=false','-p:PublishReadyToRun=false','-p:DebugType=None','-p:DebugSymbols=false',*base,'-o',publish]],
      'package':[[p,'-E','tools/package_binary.py','--publish',publish,'--output',binaries,'--rid',rid,'--runtime-pack',a['runtimePackDirectory'],'--commit',git('rev-parse','HEAD'),'--sdk','11.0.100']],
      'smoke':[[p,'-I',self_file,'--internal-extract'],[p,'-E','tools/check_binary.py','--package',extracted,'--work',str(REPO/'artifacts/tech-refresh'/rid/'apphost cwd with spaces'),'--commit',git('rev-parse','HEAD'),'--receipt',str(REPO/'artifacts/tech-refresh'/rid/'binary-smoke.json')]],
      'audit':[[d,'list',tests,'package','--vulnerable','--include-transitive','--format','json']],
    }
    need(phase in commands,'PHASE_REQUIRES_SEPARATE_FIXED_NATIVE_PACKAGE_ADMISSION')
    return [(argv,300 if phase=='policy' else 1200 if phase in ('publish','package','smoke') else 600) for argv in commands[phase]]


def usage_bytes(root):
    size=0
    for folder,dirs,files in os.walk(root,followlinks=False):
        need(len(dirs)+len(files)<=10000,'OUTPUT_MEMBER_CAP')
        for name in dirs+files:
            path=Path(folder)/name
            need(not path.is_symlink() and not getattr(path.lstat(),'st_file_attributes',0)&0x400,'OUTPUT_LINK')
        for name in files:
            size+=(Path(folder)/name).stat().st_size
            need(size<=SCRATCH_BYTES,'SCRATCH_BUDGET')
    return size


def execute(phase,a,sdk):
    global CURRENT_GROUP,CURRENT_ENV
    import shutil
    need(shutil.disk_usage(WORK).free>=FREE_BYTES,'FREE_SCRATCH')
    token=secrets.token_hex(16);start=process_start();begin=time.monotonic()
    receipts=STATE/'native-receipts';receipts.mkdir(exist_ok=True)
    previous=list(receipts.glob('*/terminal.json'));need(len(previous)<=256,'LIFECYCLE_RECEIPT_CAP')
    consumed=sum(load(p).get('durationSeconds',0) for p in previous)
    need(consumed<5400,'LOCAL_LIFECYCLE_BUDGET')
    log=receipts/token;log.mkdir()
    env={k:os.environ[k] for k in ['SystemRoot','WINDIR','COMSPEC','WSL_DISTRO_NAME','LANG','LC_ALL'] if k in os.environ}
    cache=STATE/'cache'/a['rid'];cache.mkdir(parents=True,exist_ok=True)
    for name in ['home','packages','http','plugins','tmp']:(cache/name).mkdir(exist_ok=True)
    env.update(DOTNET_ROOT=str(sdk.parent),DOTNET_HOST_PATH=str(sdk),DOTNET_CLI_HOME=str(cache/'home'),NUGET_PACKAGES=str(cache/'packages'),NUGET_HTTP_CACHE_PATH=str(cache/'http'),NUGET_PLUGINS_CACHE_PATH=str(cache/'plugins'),TEMP=str(cache/'tmp'),TMP=str(cache/'tmp'),TMPDIR=str(cache/'tmp'),DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true',MSBuildEnableWorkloadResolver='false',DOTNET_CLI_USE_MSBUILD_SERVER='0',MSBUILDDISABLENODEREUSE='1',DOTNET_SYSTEM_IO_DISABLEFILELOCKING='0',INTEGRITY03_CONTROL_NONCE=token)
    env['HOME']=str(cache/'home');env['USERPROFILE']=str(cache/'home');env['DOTNET_MULTILEVEL_LOOKUP']='0'
    env['PATH']=a['trustedToolPath']
    owner={'projectId':'03','randomToken':token,'ownerOS':platform.system(),'ownerPID':os.getpid(),'ownerProcessStartIdentity':start,'phase':phase,'startedAtUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'receiptDirectory':str(log)}
    with LEASE.open('x',encoding='utf-8') as f:json.dump(owner,f);f.flush();os.fsync(f.fileno())
    group=None;terminal=False;record={'status':'INCOMPLETE','owner':owner,'admittedSDKEntry':{'path':str(sdk),'sha256':sha(sdk,32*1024*1024)},'commands':[]}
    try:
        group=WindowsJob(a,token) if os.name=='nt' else LinuxGroup(a,token)
        CURRENT_GROUP=group;CURRENT_ENV=env
        record['controlsBefore']=group.controls()
        record['source']=source_binding(a)
        (STATE/'runtime-locks').mkdir(exist_ok=True)
        if phase in ('locks','runtime-locks'):
            markers=STATE/'transition-attempts';markers.mkdir(exist_ok=True)
            marker='source-locks.json' if phase=='locks' else 'runtime-locks-'+a['rid']+'.json'
            emit(markers/marker,{'token':token,'admissionPhase':phase,'note':'One intentional source transition globally, or one RID transition per native RID. Failure requires changed-cause review, never automatic marker reset/unlocked retry.'})
        # Probe has no SDK invocation but still needs the actual input/profile
        # admission. No command or SDK can be substituted through CLI arguments.
        counter=[0];mutex=threading.Lock()
        for index,(argv,seconds) in enumerate(recipes(phase,sdk,a['rid'],env,a)):
            need(time.monotonic()-begin<PHASES[phase],'OUTER_PHASE_DEADLINE')
            proc=group.spawn(argv,env);cmd={'argv':argv,'pid':proc.pid,'startedMonotonic':time.monotonic()}
            record['commands'].append(cmd);overflow=threading.Event();pump_errors=[]
            def pump(stream,name):
                try:
                    with (log/name).open('xb') as sink:
                        for block in iter(lambda:stream.read(65536),b''):
                            with mutex:
                                remaining=LOG_BYTES-counter[0]
                                sink.write(block[:max(0,remaining)]);counter[0]+=len(block)
                                if counter[0]>LOG_BYTES:overflow.set()
                            if overflow.is_set():break
                except BaseException as error:pump_errors.append(type(error).__name__);overflow.set()
            threads=[threading.Thread(target=pump,args=(stream,f'{index}-{label}.log'),daemon=True) for stream,label in [(proc.stdout,'stdout'),(proc.stderr,'stderr')]]
            for thread in threads:thread.start()
            reason=None
            while proc.poll() is None:
                if overflow.is_set():reason='LOG_CAP';break
                if time.monotonic()-cmd['startedMonotonic']>seconds or time.monotonic()-begin>PHASES[phase] or consumed+time.monotonic()-begin>5400:reason='DEADLINE';break
                used=usage_bytes(STATE)+(usage_bytes(REPO/'artifacts') if (REPO/'artifacts').is_dir() else 0)
                need(used<=SCRATCH_BYTES,'COMBINED_SCRATCH_BUDGET')
                time.sleep(0.1)
            if not reason and group.active():reason='DESCENDANTS_AFTER_ROOT'
            if reason:group.stop()
            proc.wait(timeout=10)
            for thread in threads:thread.join(timeout=10)
            need(not any(t.is_alive() for t in threads),'LOG_THREADS_NOT_TERMINAL')
            need(not pump_errors,'LOG_WRITER_FAILED')
            deadline=time.monotonic()+10
            while group.active() and time.monotonic()<deadline:time.sleep(0.05)
            need(not group.active(),'DESCENDANTS_NOT_TERMINAL')
            cmd.update(exit=proc.returncode,stopReason=reason,logBytes=counter[0],durationSeconds=time.monotonic()-cmd['startedMonotonic'])
            if phase=='probe-deadline':need(reason=='DEADLINE','NEGATIVE_PROBE_DID_NOT_REACH_DEADLINE')
            else:need(reason is None and proc.returncode==0,'TARGET_FAILURE_OR_RESOURCE_LIMIT')
            if phase=='sdk-identity':need((log/f'{index}-stdout.log').read_text().strip()=='11.0.100','ACTUAL_CONSUMER_SDK_VERSION')
        record['status']='NATIVE_PHASE_COMMANDS_COMPLETED'
    except BaseException as error:
        record['status']='INCOMPLETE_OR_FAILED';record['failureCode']=str(error) if isinstance(error,Hold) else type(error).__name__
        if group is not None:group.stop()
        raise
    finally:
        if group is not None:
            limit=time.monotonic()+10
            while group.active() and time.monotonic()<limit:time.sleep(0.05)
            terminal=not group.active();record['controlsAfter']=group.controls()
            if terminal:group.close()
        record['ownedTargetsTerminal']=terminal;record['durationSeconds']=time.monotonic()-begin
        emit(log/'terminal.json',record)
        if terminal:
            current=load(LEASE)
            need(all(current.get(k)==owner[k] for k in ['projectId','randomToken','ownerPID','ownerProcessStartIdentity']),'LEASE_OWNER_CHANGED')
            LEASE.unlink()
    return log/'terminal.json'


def extract_child():
    need(os.environ.get('INTEGRITY03_CONTROL_NONCE'),'EXTRACTION_REQUIRES_CONTROLLER')
    rid='win-x64' if os.name=='nt' else 'linux-x64'
    prefix=f'market-data-integrity-lab-v0.6.2-preview.1-{rid}'
    base=REPO/'artifacts/tech-refresh'/rid
    archive=base/'binary'/(prefix+('.zip' if os.name=='nt' else '.tar.gz'))
    ordinary(archive,REPO);need(archive.stat().st_size<=512*1024*1024,'ARCHIVE_RAW_CAP')
    target=base/'extracted package with spaces';need(not target.exists(),'REFUSE_EXISTING_EXTRACTION')
    opened=zipfile.ZipFile(archive) if os.name=='nt' else tarfile.open(archive,'r:gz')
    with opened as bundle:
        entries=bundle.infolist() if os.name=='nt' else bundle.getmembers()
        need(len(entries)<=4096,'ARCHIVE_MEMBER_CAP')
        seen=set();total=0;checked=[]
        for entry in entries:
            name=entry.filename if os.name=='nt' else entry.name
            size=entry.file_size if os.name=='nt' else entry.size
            need('\\' not in name and ':' not in name and not name.startswith('/') and '..' not in Path(name).parts and name.split('/')[0]==prefix,'ARCHIVE_PATH')
            need(name not in seen,'ARCHIVE_DUPLICATE');seen.add(name)
            if os.name=='nt':need(not stat.S_ISLNK((entry.external_attr>>16)&0xffff),'ARCHIVE_LINK')
            else:need(entry.isdir() or entry.isfile(),'ARCHIVE_SPECIAL_MEMBER')
            need(size<=64*1024*1024,'ARCHIVE_FILE_CAP');total+=size;need(total<=1024*1024*1024,'ARCHIVE_EXPANDED_CAP')
            checked.append(entry)
        target.mkdir()
        for entry in checked:
            name=entry.filename if os.name=='nt' else entry.name
            dest=target/name
            isdir=entry.is_dir() if os.name=='nt' else entry.isdir()
            if isdir:dest.mkdir(parents=True,exist_ok=True);continue
            dest.parent.mkdir(parents=True,exist_ok=True)
            source=bundle.open(entry) if os.name=='nt' else bundle.extractfile(entry)
            with source as inp,dest.open('xb') as out:
                count=0
                for block in iter(lambda:inp.read(65536),b''):
                    count+=len(block);need(count<=(entry.file_size if os.name=='nt' else entry.size),'ARCHIVE_STREAM_LENGTH');out.write(block)
                need(count==(entry.file_size if os.name=='nt' else entry.size),'ARCHIVE_SHORT_MEMBER')
            if os.name!='nt':dest.chmod(entry.mode&0o777)
    print(json.dumps({'archiveSha256':sha(archive,512*1024*1024),'memberCount':len(checked),'expandedBytes':total,'target':str(target)}))


def probe_child(mode):
    need(os.environ.get('INTEGRITY03_CONTROL_NONCE'),'PROBE_REQUIRES_CONTROLLER')
    # Bounded harmless descendant, no SDK/network/files/lease. The deadline case
    # must be terminated by the owning kernel group, never process-name killing.
    child=subprocess.Popen([sys.executable,'-I','-c','import time; time.sleep(60)'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    print(json.dumps({'probe':'sdk-free','pid':os.getpid(),'childPID':child.pid,'uid':os.getuid() if os.name!='nt' else None,'cwd':os.getcwd(),'cgroup':Path('/proc/self/cgroup').read_text() if os.name!='nt' else None}),flush=True)
    if mode=='deadline':time.sleep(60)
    else:child.terminate();child.wait(timeout=5)


if __name__=='__main__':
    try:
        if os.environ.get('RUNNER_ENVIRONMENT')!='github-hosted':raise Hold('LOCAL_HOST_SDK11_EXCLUDED')
        REPO=Path(os.environ['GITHUB_WORKSPACE']).resolve(strict=True)
        from supported_ci_route_03 import entry
        entry(WindowsJob)
    except (Hold,OSError,ValueError,KeyError,TypeError) as error:
        print(json.dumps({'status':'HOLD_INCOMPLETE_NOT_GLOBAL_READY','code':str(error) if isinstance(error,Hold) else type(error).__name__}),file=sys.stderr)
        sys.exit(70)
