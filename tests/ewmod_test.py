"""LuaJIT host tests for direct ewmod reads. No game is launched.

Optional arguments: public GameModule.lua and an upstream MOAI VFS probe DLL.
Lupa and the probe are test dependencies only.
"""
import copy
import ctypes
import hashlib
import json
import os
import sys
import tempfile
import zipfile
from pathlib import Path
from lupa.luajit21 import LuaRuntime

loader = Path(__file__).resolve().parents[1]
module_source = (loader / 'ewmod.lua').read_bytes()
overlay_source = (loader / 'overlay.lua').read_bytes()
game_module = Path(sys.argv[1]).read_bytes() if len(sys.argv) > 1 else None
probe = ctypes.CDLL(sys.argv[2]) if len(sys.argv) > 2 else None
initial_directory = Path.cwd()
if probe:
    probe.probe_init.argtypes = [ctypes.c_char_p]
    probe.probe_mount.argtypes = [ctypes.c_char_p, ctypes.c_char_p]
    probe.probe_absolute.argtypes = [ctypes.c_char_p]
    probe.probe_absolute.restype = ctypes.c_char_p
    probe.probe_read.argtypes = [ctypes.c_char_p, ctypes.c_void_p, ctypes.c_size_t]
    probe.probe_read.restype = ctypes.c_longlong
    probe.probe_list.argtypes = [ctypes.c_char_p, ctypes.c_int]
    probe.probe_list.restype = ctypes.c_char_p

def digest(data): return hashlib.sha256(data).hexdigest()
def archive(path, entries):
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as output:
        for key, value in entries.items(): output.writestr(key, value)

with tempfile.TemporaryDirectory(prefix='Eastward-loader-vfs-') as temporary:
    root = Path(temporary)
    original = b'original DLC resource'
    replacement = '한국어 日本語'.encode()
    payloads = {'arc/value.bin': replacement, 'arc/new/font.ttf': b'new font resource',
                'arc/new/script.lua': b"value = 'from ZIP'", 'arc/new/data.json': b'{"value":42}'}
    metadata = {name: {'originalHash': digest(original) if name == 'arc/value.bin' else None,
                       'hash': digest(value), 'size': len(value)} for name, value in payloads.items()}
    manifest = {'format': 'eastward-mod', 'version': 1, 'name': 'test', 'resources': metadata}
    package = root / 'mods/packages/test.ewmod'
    members = {'manifest.json': json.dumps(manifest).encode(),
               **{'resources/' + name: value for name, value in payloads.items()}}
    archive(package, members)
    config = {'format': 'eastward-loader', 'version': 1,
              'mods': [{'id': 'test', 'kind': 'ewmod', 'file': 'packages/test.ewmod', 'enabled': True}],
              'deployment': {'mode': 'ewmod', 'packages': [{'id': 'test', 'sha256': digest(package.read_bytes())}],
                             'resources': [{'path': name, **value} for name, value in metadata.items()]}}
    count = 0

    def run(configuration, baseline=original, expect=True, broken_interface=False):
        global count
        lua = LuaRuntime(encoding=None, unpack_returned_tuples=True)
        mounted, calls = {}, []
        archive(root / 'baseline.zip', {'value.bin': baseline, 'unrelated.bin': b'untouched'})
        system = root / '_system/__eastward_modloader/overlay.lua'
        system.parent.mkdir(parents=True, exist_ok=True)
        system.write_bytes(overlay_source)
        if probe: probe.probe_init(str(root).encode())
        null = lua.eval(b'{}')
        def convert(value):
            if value is None: return null
            if isinstance(value, dict): return lua.table_from({key.encode(): convert(item) for key, item in value.items()})
            if isinstance(value, list): return lua.table_from([convert(item) for item in value])
            if isinstance(value, str): return value.encode()
            return value
        def absolute(path):
            if probe: return probe.probe_absolute(path)
            return os.path.normpath(root / path.decode()).replace('\\', '/').encode()
        def relative(path):
            value = absolute(path).decode()
            return os.path.relpath(value, root).replace('\\', '/')
        def read(path):
            if not isinstance(path, bytes): return None
            if probe:
                buffer = ctypes.create_string_buffer(16 * 1024 * 1024)
                size = probe.probe_read(path, buffer, len(buffer))
                return buffer.raw[:size] if size >= 0 else None
            path = relative(path)
            prefixes = [prefix for prefix in mounted if path.startswith(prefix + '/')]
            if prefixes:
                prefix = max(prefixes, key=len)
                return mounted[prefix].get(path[len(prefix) + 1:])
            file = root / path
            return file.read_bytes() if file.is_file() else None
        def mount(prefix, filename=None):
            name = relative(prefix)
            calls.append((name, filename))
            if probe and probe.probe_mount(prefix, filename) != 0: return False
            if filename is None: mounted.pop(name, None); return True
            try:
                with zipfile.ZipFile(root / relative(filename)) as zipped:
                    mounted[name] = {member: zipped.read(member) for member in zipped.namelist()}
            except zipfile.BadZipFile: return False
            return True
        def listing(path, dirs):
            if probe:
                return lua.table_from(probe.probe_list(path, dirs).splitlines())
            path = relative(path)
            names = set()
            for prefix, entries in mounted.items():
                for entry in entries:
                    full = prefix + '/' + entry
                    if full.startswith(path + '/'):
                        member = full[len(path) + 1:]
                        if dirs and '/' in member: names.add(member.split('/')[0])
                        elif not dirs and '/' not in member: names.add(member)
            return lua.table_from([name.encode() for name in sorted(names)])
        mount(b'arc', b'baseline.zip')
        calls.clear()
        globals = lua.globals()
        globals[b'host_read'] = read
        globals[b'host_absolute'] = absolute
        globals[b'host_mount'] = mount
        globals[b'host_files'] = lambda path: listing(path, False)
        globals[b'host_dirs'] = lambda path: listing(path, True)
        globals[b'host_hash'] = lambda data: digest(data).encode()
        globals[b'host_json'] = lambda data: convert(json.loads(data))
        globals[b'null_value'] = null
        lua.execute(b"""
        function class(interface)
            return {getInterfaceTable=function()return interface end,
                new=function()return setmetatable({}, {__index=interface})end}
        end
        MOAIFileStream=class{
            open=function(self,path,mode)self.path=path;self.data=host_read(path);self.position=1;return self.data~=nil end,
            read=function(self,size)if not self.data then return nil end
                if not size then return self.data end
                local data=self.data:sub(self.position,self.position+size-1);self.position=self.position+#data;return data end,
            close=function()end}
        MOAIFileStream.READ=1;MOAIFileStream.READ_WRITE=2
        for _, name in ipairs{'MOAIDataBuffer','MOAIImage','MOAITextureBase','MOAITexture','MOAIFont','MOAIMovieClip','MOAISpineSkeletonData','MOAIFMODStudioSystem'} do
            _G[name]=class{load=function(self,path,...)self.path=path;return host_read(path), ... end,
                loadAsync=function(self,path,...)self.path=path;return host_read(path), ... end,
                loadFromBMFont=function(self,path,...)self.path=path;return host_read(path), ... end,
                loadWithAtlas=function(self,path,...)self.path=path;return host_read(path), ... end,
                loadBankFile=function(self,path,...)self.path=path;return host_read(path), ... end}
        end
        MOAIHashWriter={new=function()return{
            openSHA256=function(self)self.parts={};return true end,
            write=function(self,data)self.parts[#self.parts+1]=data end,close=function()end,
            getHashHex=function(self)return host_hash(table.concat(self.parts))end}end}
        MOAIJsonParser={JSON_NULL=null_value,decode=host_json,load=function(path)return host_json(host_read(path))end}
        MOAIFileSystem={checkFileExists=function(path)return host_read(path)~=nil end,
            checkPathExists=function()return false end,
            getAbsoluteFilePath=function(path)return host_absolute(path)end,
            loadFile=function(path)return host_read(path)end,
            getVirtualPathInfo=function(path)return path,host_read(path)end,
            mountVirtualDirectory=function(...)return host_mount(...)end,
            listFiles=function(path)return host_files(path)end,listDirectories=function(path)return host_dirs(path)end}
        io={open=function(path,mode)return{path=path,data=host_read(path),read=function(self)return self.data end}end}
        loadfile=function(path)return loadstring(host_read(path))end
        dofile=function(path)return assert(loadfile(path))()end
        GameModule={addGameModuleMapping=function(name,path,origin)lastMapping={name,path,origin}end}
        _stat=function()end;_warn=function()end
        precreated=MOAIFileStream.new()
        originalOpen=MOAIFileStream.getInterfaceTable().open
        originalExists=MOAIFileSystem.checkFileExists
        """)
        if game_module: lua.execute(game_module)
        if broken_interface: lua.execute(b'MOAIFileStream.getInterfaceTable=nil')
        module = lua.execute(module_source)
        try:
            try:
                module.activate(convert(configuration))
            except Exception:
                if expect: raise
                assert list(mounted) == ['arc'], 'Failed validation must unmount private packages only'
                lua.execute(b'assert(MOAIFileStream.getInterfaceTable == nil or MOAIFileStream.getInterfaceTable().open == originalOpen);assert(MOAIFileSystem.checkFileExists == originalExists)')
                return
            assert expect, 'Invalid package was accepted'
            assert read(b'arc/value.bin') == baseline, 'Original native mount must remain unchanged'
            assert all(name.startswith('__eastward_modloader_packages/') for name, _ in calls), 'Must never remount original game aliases'
            globals[b'expected_replacement'] = replacement
            lua.execute(b"""
            assert(precreated:open('arc/value.bin') and precreated:read()==expected_replacement)
            assert(precreated.path:find('/resources/arc/value.bin',1,true))
            assert(precreated:open('./arc/value.bin') and precreated:read()==expected_replacement)
            assert(MOAIFileSystem.loadFile(MOAIFileSystem.getAbsoluteFilePath('arc/value.bin'))==expected_replacement)
            assert(MOAIFileSystem.loadFile('arc/unrelated.bin')=='untouched')
            assert(MOAIFileSystem.loadFile('arc/missing.bin')==nil)
            assert(MOAIFileSystem.checkFileExists('arc/new/font.ttf'))
            assert(MOAIFileSystem.checkPathExists('arc/new'))
            assert(table.concat(MOAIFileSystem.listFiles('arc'),',')=='unrelated.bin,value.bin')
            assert(table.concat(MOAIFileSystem.listDirectories('arc'),',')=='new')
            assert(table.concat(MOAIFileSystem.listFiles('arc/new'),',')=='data.json,font.ttf,script.lua')
            for _, name in ipairs{'MOAIDataBuffer','MOAIImage','MOAITextureBase','MOAITexture','MOAIFont','MOAIMovieClip'} do
                local object=_G[name].new()
                local data, argument=object:load('arc/value.bin',123)
                assert(data==expected_replacement and argument==123)
                assert(object:load('arc/unrelated.bin')=='untouched')
                assert(object:load({})==nil)
            end
            assert(MOAIDataBuffer.new():loadAsync('arc/value.bin')==expected_replacement)
            assert(MOAITexture.new():loadAsync('arc/value.bin')==expected_replacement)
            local textures={};local data, passed=MOAIFont.new():loadFromBMFont('arc/new/font.ttf',textures)
            assert(data=='new font resource' and passed==textures)
            assert(MOAISpineSkeletonData.new():loadWithAtlas('arc/value.bin',textures)==expected_replacement)
            assert(MOAIFMODStudioSystem.new():loadBankFile('arc/value.bin',true)==expected_replacement)
            assert(MOAIJsonParser.load('arc/new/data.json').value==42)
            assert(io.open('arc/value.bin','rb'):read()==expected_replacement)
            for _, mode in ipairs{'w','a','r+'} do assert(io.open('arc/value.bin',mode).path=='arc/value.bin')end
            precreated:open('arc/value.bin',MOAIFileStream.READ_WRITE);assert(precreated.path=='arc/value.bin')
            assert(type(loadfile('arc/new/script.lua'))=='function')
            """)
            globals[b'GameModule'].addGameModuleMapping(b'NewModule', b'arc/new/script.lua', b'editor/new.lua')
            if game_module:
                lua.execute(b"local m, errors=GameModule.loadGameModule('NewModule',true);assert(m and m.value=='from ZIP' and #errors==0)")
            else:
                lua.execute(b"assert(lastMapping[2]:find('/resources/arc/new/script.lua',1,true) and lastMapping[3]=='editor/new.lua')")
            module.overlay.uninstall()
            lua.execute(b'assert(MOAIFileStream.getInterfaceTable().open==originalOpen);assert(MOAIFileSystem.checkFileExists==originalExists)')
            assert not (root / 'mods/cache').exists(), 'No compiled G cache is needed'
        finally:
            if probe: probe.probe_cleanup()
            os.chdir(initial_directory)
            count += 1

    run(config)
    run(config, baseline=b'changed original', expect=False)
    run(config, broken_interface=True, expect=False)
    for field in ['package', 'resource', 'size', 'mode', 'missing-resource']:
        bad = copy.deepcopy(config)
        if field == 'package': bad['deployment']['packages'][0]['sha256'] = '0' * 64
        elif field == 'resource': bad['deployment']['resources'][0]['hash'] = '0' * 64
        elif field == 'size': bad['deployment']['resources'][0]['size'] += 1
        elif field == 'mode': bad['deployment']['mode'] = 'unknown'
        else: bad['deployment']['resources'].pop()
        run(bad, expect=False)
    duplicate = copy.deepcopy(config)
    duplicate['mods'].append({**duplicate['mods'][0], 'id': 'second'})
    duplicate['deployment']['packages'].append({**duplicate['deployment']['packages'][0], 'id': 'second'})
    run(duplicate)
    old = copy.deepcopy(config)
    old['deployment'].pop('mode')
    old['deployment']['archives'] = [{'name': 'arc', 'file': 'no-longer-required.g', 'sha256': '0' * 64}]
    run(old)
    for changed in [{**members, 'unexpected.txt': b'unknown'},
                    {**members, 'resources/arc/value.bin': b'corrupted'}]:
        archive(package, changed)
        bad = copy.deepcopy(config)
        bad['deployment']['packages'][0]['sha256'] = digest(package.read_bytes())
        run(bad, expect=False)
    disabled = LuaRuntime()
    module = disabled.execute(module_source)
    module.activate(disabled.table_from({'mods': [], 'deployment': {'packages': [], 'resources': []}}, recursive=True))
    print(f'{count + 1} direct ewmod checks passed' + (' using upstream native ZIP VFS.' if probe else ' using a LuaJIT host.'))
