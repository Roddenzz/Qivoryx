"""Pin direct fallback installers to Microsoft's selected winget manifests.
Run after generating artifacts/installer-metadata.json using winget show.
PyYAML is a build-time dependency only; no Python is needed by the app.
"""
import concurrent.futures, ctypes, json, pathlib, sys, urllib.request, urllib.parse
root=pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'.tools/python-packages'))
import yaml
catalog=json.loads((root/'Tweaker/catalog.json').read_text(encoding='utf-8-sig'))
metadata=json.loads((root/'artifacts/installer-metadata.json').read_text(encoding='utf-8-sig'))
by_id={p['Id']:p for p in catalog}
defaults={'inno':'/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-', 'nullsoft':'/S', 'msi':'/qn /norestart', 'wix':'/qn /norestart','burn':'/quiet /norestart'}
def windows_args(value):
    argc=ctypes.c_int()
    split=ctypes.windll.shell32.CommandLineToArgvW
    split.argtypes=[ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_int)];split.restype=ctypes.POINTER(ctypes.c_wchar_p)
    ptr=split('setup.exe '+value,ctypes.byref(argc))
    try:return [ptr[i] for i in range(1,argc.value)]
    finally:ctypes.windll.kernel32.LocalFree(ctypes.cast(ptr,ctypes.c_void_p))
def enrich(meta):
    package=by_id[meta['Id']]
    url='https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/'+meta['Id'][0].lower()+'/'+meta['Id'].replace('.','/')+'/'+urllib.parse.quote(meta['Version'],safe='')+'/'+meta['Id']+'.installer.yaml'
    try:
        with urllib.request.urlopen(url,timeout=25) as response:doc=yaml.safe_load(response)
        selected=next((i for i in doc.get('Installers',[]) if i.get('InstallerSha256','').lower()==meta['SHA256'].lower()),None)
        if selected is None:raise ValueError('selected hash absent from manifest')
        merged={**doc,**selected}
        switches={**doc.get('InstallerSwitches',{}),**selected.get('InstallerSwitches',{})}
        kind=merged.get('InstallerType','');nested=merged.get('NestedInstallerType','')
        installer_kind=nested if kind=='zip' else kind
        silent=switches.get('Silent',switches.get('SilentWithProgress',defaults.get(installer_kind,'')))
        silent_only=merged.get('InstallModes',[])==['silent']
        if not silent and meta['Id']=='Discord.Discord':silent='--silent'
        custom=switches.get('Custom','')
        if installer_kind not in ('portable','msix','appx') and not silent and not silent_only:raise ValueError('no documented silent switches')
        args=windows_args(str(silent)+' '+str(custom))
        if any('<' in a or '>' in a for a in args):raise ValueError('installer needs unresolved placeholders')
        package.update(Url=merged['InstallerUrl'],SHA256=merged['InstallerSha256'].upper(),Version=meta['Version'],InstallerType=installer_kind,Args=args,Manifest=url)
        package['SuccessCodes']=list(merged.get('InstallerSuccessCodes',[]))+[c['InstallerReturnCode'] for c in merged.get('ExpectedReturnCodes',[]) if c.get('ReturnResponse')=='alreadyInstalled']
        package.pop('Signer',None)
        package['Extension']='zip' if kind=='zip' else 'msi' if installer_kind in ('msi','wix') else 'msix' if installer_kind in ('msix','appx') else 'exe'
        actual_suffix=pathlib.PurePosixPath(urllib.parse.urlparse(merged['InstallerUrl']).path).suffix.lower().lstrip('.')
        if installer_kind in ('msix','appx') and actual_suffix in ('msix','appx','msixbundle','appxbundle'):package['Extension']=actual_suffix
        nested_files=merged.get('NestedInstallerFiles',[])
        if kind=='zip':
            if not nested_files:raise ValueError('nested file missing')
            package['NestedPath']=nested_files[0]['RelativeFilePath']
            package['PortableAlias']=nested_files[0].get('PortableCommandAlias','')
        return dict(Id=meta['Id'],ok=True,type=installer_kind)
    except Exception as error:
        return dict(Id=meta['Id'],ok=False,error=str(error),existing_direct=bool(package.get('Url')))
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:results=list(pool.map(enrich,metadata))
(root/'Tweaker/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
(root/'artifacts/fallback-manifests.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
print('Pinned manifests:',sum(r['ok'] for r in results),'Direct fallbacks:',sum(bool(p.get('Url')) for p in catalog))
for result in results:
    if not result['ok']:print(result)
