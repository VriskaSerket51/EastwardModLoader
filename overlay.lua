--------------------------------------------------------------------
-- Route changed files to private ZIP mounts; original mounts stay intact.
--------------------------------------------------------------------
local module = {}

function module.install(resources)
    local fs = MOAIFileSystem
    local absolute = assert(fs.getAbsoluteFilePath, 'Native absolute paths are required')
    local files, directories, changes = {}, {}, {}
    local function key(filename)
        if type(filename) ~= 'string' or #filename > 4096 or filename:find('%z') then return nil end
        return absolute(filename):gsub('\\', '/'):gsub('/+$', '')
    end
    for filename, target in pairs(resources) do
        local full = assert(key(filename))
        files[full] = absolute(target)
        local child = full:match('[^/]+$')
        local parent = full:match('^(.*)/[^/]+$')
        local isDirectory = false
        while parent and child do
            local entry = directories[parent]
            if not entry then entry = { files = {}, directories = {} }; directories[parent] = entry end
            (isDirectory and entry.directories or entry.files)[child] = true
            child, parent = parent:match('[^/]+$'), parent:match('^(.*)/[^/]+$')
            isDirectory = true
        end
    end
    local function resolve(filename)
        return files[key(filename)] or filename
    end
    local function replace(container, name, wrapper)
        local original = container and container[name]
        if type(original) ~= 'function' then return false end
        for _, change in ipairs(changes) do
            if change.container == container and change.name == name then return true end
        end
        changes[#changes + 1] = { container = container, name = name, previous = rawget(container, name), value = wrapper(original) }
        return true
    end
    local function readFunction(original)
        return function(filename, ...) return original(resolve(filename), ...) end
    end
    for _, name in ipairs({ 'checkFileExists', 'loadFile', 'getVirtualPathInfo' }) do
        replace(fs, name, readFunction)
    end
    replace(fs, 'checkPathExists', function(original)
        return function(directory, ...) return directories[key(directory)] ~= nil or original(directory, ...) end
    end)
    for _, name in ipairs({ 'listFiles', 'listDirectories' }) do
        local field = name == 'listFiles' and 'files' or 'directories'
        replace(fs, name, function(original)
            return function(directory, ...)
                local result = original(directory, ...)
                local added = directories[key(directory)]
                if not added then return result end
                local found, merged = {}, {}
                for _, item in ipairs(result or {}) do found[item:gsub('/$', '')] = true; merged[#merged + 1] = item end
                for item in pairs(added[field]) do if not found[item] then merged[#merged + 1] = item end end
                table.sort(merged)
                return merged
            end
        end)
    end
    local classes = {
        MOAIFileStream = { 'open' }, MOAIDataBuffer = { 'load', 'loadAsync' },
        MOAIImage = { 'load', 'loadAsync', 'loadPVR', 'loadKTX' },
        MOAITextureBase = { 'load', 'loadAsync' }, MOAITexture = { 'load', 'loadAsync' },
        MOAIFont = { 'load', 'loadFromBMFont', 'loadFromBDF' },
        MOAIMovieClip = { 'load' },
        MOAISpineSkeletonData = { 'loadWithAtlas' },
        MOAIFMODStudioSystem = { 'loadBankFile' }
    }
    for className, names in pairs(classes) do
        local class = rawget(_G, className)
        if class then
            assert(type(class.getInterfaceTable) == 'function', className .. ' interface unavailable')
            local interface = class.getInterfaceTable()
            assert(type(interface) == 'table', className .. ' interface unavailable')
            for _, name in ipairs(names) do
                local readOnly = className == 'MOAIFileStream' and name == 'open'
                local replaced = replace(interface, name, function(original)
                    return function(self, filename, ...)
                        local mode = select(1, ...)
                        if not readOnly or mode == nil or mode == MOAIFileStream.READ then filename = resolve(filename) end
                        return original(self, filename, ...)
                    end
                end)
                if readOnly then assert(replaced, 'MOAIFileStream.open is required') end
            end
        end
    end
    assert(MOAIFileStream and MOAIFileStream.getInterfaceTable, 'Native file streams are required')
    replace(MOAIJsonParser, 'load', readFunction)
    replace(MOAIXmlParser, 'parseFile', readFunction)
    replace(MOAIFmodEventMgr, 'loadProject', readFunction)
    if io then
        replace(io, 'open', function(original)
            return function(filename, mode, ...)
                if mode == nil or (mode:sub(1, 1) == 'r' and not mode:find('+', 1, true)) then filename = resolve(filename) end
                return original(filename, mode, ...)
            end
        end)
    end
    replace(_G, 'loadfile', readFunction)
    replace(_G, 'dofile', readFunction)
    -- GameModule captures checkFileExists before installation; map its filenames too.
    replace(GameModule, 'addGameModuleMapping', function(original)
        return function(name, filename, ...) return original(name, resolve(filename), ...) end
    end)
    local function uninstall()
        for index = #changes, 1, -1 do
            local change = changes[index]
            change.container[change.name] = change.previous
        end
    end
    local ok, reason = pcall(function()
        for _, change in ipairs(changes) do change.container[change.name] = change.value end
    end)
    if not ok then uninstall(); error(reason, 0) end
    return { resolve = resolve, uninstall = uninstall }
end

return module
