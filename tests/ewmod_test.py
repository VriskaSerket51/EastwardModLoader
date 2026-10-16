"""LuaJIT host tests for native-VFS ewmod activation; no game is launched.
Requires the optional Lupa test host, not a loader runtime dependency.
"""
import hashlib
import json
import tempfile
import zipfile
from pathlib import Path
from lupa.luajit21 import LuaRuntime

module_source = (Path(__file__).resolve().parents[1] / 'ewmod.lua').read_bytes()
def digest(data): return hashlib.sha256(data).hexdigest()
def archive(path, entries):
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as output:
        for key, value in entries.items(): output.writestr(key, value)

with tempfile.TemporaryDirectory(prefix='Eastward-loader-vfs-') as temporary:
    root = Path(temporary)
    original, replacement, font = b'original DLC resource', '한국어 日本語'.encode(), b'new font resource'
    metadata = {
        'arc/value.bin': {'originalHash': digest(original), 'hash': digest(replacement), 'size':len(replacement)},
        'arc/new.ttf': {'originalHash': None, 'hash': digest(font), 'size':len(font)}
    }
    manifest = {'format':'eastward-mod','version':1,'name':'test','resources':metadata}
    package = root / 'mods/packages/test.ewmod'
    members = {'manifest.json':json.dumps(manifest).encode(), 'resources/arc/value.bin':replacement,'resources/arc/new.ttf':font}
    archive(package,members)
    cache = root / 'mods/cache/generation/arc.g'
    # Host adapter uses ZIP for a readable native archive fixture. Production uses Core's GArchive writer.
    archive(cache,{'value.bin':replacement,'new.ttf':font})
    config = {'format':'eastward-loader','version':1,'mods':[{'id':'test','kind':'ewmod','file':'packages/test.ewmod','enabled':True}],
        'deployment': {'packages':[{'id':'test','sha256':digest(package.read_bytes())}],
            'resources':[{'path':name,**value} for name,value in metadata.items()],
            'archives':[{'name':'arc','file':'cache/generation/arc.g','sha256':digest(cache.read_bytes())}]}}

    def run(configuration, baseline=original, expect=True):
        lua=LuaRuntime(encoding=None,unpack_returned_tuples=True)
        mounted, calls = {}, []
        null=lua.eval(b'{}')
        def convert(value):
            if value is None:return null
            if isinstance(value,dict):return lua.table_from({key.encode():convert(item) for key,item in value.items()})
            if isinstance(value,list):return lua.table_from([convert(item) for item in value])
            if isinstance(value,str):return value.encode()
            return value
        def read(path):
            path=path.decode()
            for prefix, entries in mounted.items():
                if path.startswith(prefix+'/'):return entries.get(path[len(prefix)+1:])
            if path=='arc/value.bin':return baseline
            file=root/path
            return file.read_bytes() if file.is_file() else None
        def mount(prefix,filename=None):
            prefix=prefix.decode();calls.append(prefix)
            if filename is None:mounted.pop(prefix,None);return True
            file=root/filename.decode()
            try:
                with zipfile.ZipFile(file) as zipped:mounted[prefix]={name:zipped.read(name) for name in zipped.namelist()}
            except zipfile.BadZipFile:return False
            return True
        def listing(path,dirs):
            path=path.decode();names=set()
            for prefix,entries in mounted.items():
                for entry in entries:
                    full=prefix+'/'+entry
                    if full.startswith(path+'/'):
                        relative=full[len(path)+1:]
                        if dirs and '/' in relative:names.add(relative.split('/')[0])
                        elif not dirs and '/' not in relative:names.add(relative)
            return lua.table_from([name.encode() for name in sorted(names)])
        lua.globals()[b'host_read']=read
        lua.globals()[b'host_mount']=mount
        lua.globals()[b'host_files']=lambda path:listing(path,False)
        lua.globals()[b'host_dirs']=lambda path:listing(path,True)
        lua.globals()[b'host_hash']=lambda data:digest(data).encode()
        lua.globals()[b'host_json']=lambda data:convert(json.loads(data))
        lua.globals()[b'null_value']=null
        lua.execute(b"""
        MOAIFileStream={READ=1,new=function()return{
            open=function(self,path)self.data=host_read(path);self.position=1;return self.data~=nil end,
            read=function(self,size)if not self.data then return nil end
                if not size then return self.data end
                local data=self.data:sub(self.position,self.position+size-1);self.position=self.position+#data;return data end,
            close=function()end}end}
        MOAIHashWriter={new=function()return{
            openSHA256=function(self)self.parts={};return true end,
            write=function(self,data)self.parts[#self.parts+1]=data end,close=function()end,
            getHashHex=function(self)return host_hash(table.concat(self.parts))end}end}
        MOAIJsonParser={JSON_NULL=null_value,decode=host_json}
        MOAIFileSystem={checkFileExists=function(path)return host_read(path)~=nil end,
            mountVirtualDirectory=host_mount,listFiles=host_files,listDirectories=host_dirs}
        """)
        module=lua.execute(module_source)
        try:
            module.activate(convert(configuration))
            assert expect,'Invalid package was accepted'
            assert read(b'arc/value.bin')==replacement and read(b'arc/new.ttf')==font
            assert calls[-1]=='arc'
        except Exception:
            if expect:raise
            assert 'arc' not in calls,'Game cache was mounted before all validations completed'
        return calls

    run(config)
    run(config,baseline=b'changed original',expect=False)
    bad=json.loads(json.dumps(config));bad['deployment']['archives'][0]['sha256']='0'*64;run(bad,expect=False)
    bad=json.loads(json.dumps(config));bad['deployment']['packages'][0]['sha256']='0'*64;run(bad,expect=False)
    bad=json.loads(json.dumps(config));bad['deployment']['resources'][0]['hash']='0'*64;run(bad,expect=False)
    archive(package,{**members,'unexpected.txt':b'unknown'})
    bad=json.loads(json.dumps(config));bad['deployment']['packages'][0]['sha256']=digest(package.read_bytes());run(bad,expect=False)
    archive(package,{**members,'resources/arc/value.bin':b'corrupted'})
    bad=json.loads(json.dumps(config));bad['deployment']['packages'][0]['sha256']=digest(package.read_bytes());run(bad,expect=False)
    disabled={'format':'eastward-loader','version':1,'mods':[],'deployment':{'packages':[],'archives':[],'resources':[]}}
    # No active packages do not change native game mounts.
    lua=LuaRuntime();module=lua.execute(module_source);module.activate(lua.table_from(disabled,recursive=True))
print('ewmod LuaJIT VFS checks passed: source/manifest/resource/original/cache hashes, extra ZIP entries, JSON_NULL new resources, validation before mount, disabled packages.')
