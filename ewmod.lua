--------------------------------------------------------------------
-- Mount resource packages privately and route changed file reads to their ZIP entries.
--------------------------------------------------------------------
local module = {}
local function fail(message) error('[EastwardModLoader] ' .. message, 0) end
local function path(value)
    if type(value) ~= 'string' or value == '' or value:find('\\', 1, true) or value:find(':', 1, true)
        or value:sub(1,1) == '/' or value:find('%z') then fail('Invalid relative path') end
    for part in value:gmatch('[^/]+') do if part == '.' or part == '..' then fail('Invalid relative path: ' .. value) end end
    if value:find('//', 1, true) or value:sub(-1) == '/' then fail('Invalid relative path: ' .. value) end
    return value
end
local function hash(value)
    if type(value) ~= 'string' or #value ~= 64 or value:find('[^%x]') then fail('Invalid SHA256') end
    return value:lower()
end
local function nullable(value)
    if value == nil or value == MOAIJsonParser.JSON_NULL then return nil end
    return value
end
local function read(filename)
    local stream = MOAIFileStream.new()
    if stream:open(filename, MOAIFileStream.READ) == false then return nil end
    local data = stream:read()
    stream:close()
    return data
end
local function digest(filename)
    local stream = MOAIFileStream.new()
    if stream:open(filename, MOAIFileStream.READ) == false then fail('Cannot read ' .. filename) end
    local writer = MOAIHashWriter.new()
    assert(writer:openSHA256() ~= false, 'SHA256 writer could not be opened')
    local size = 0
    while true do
        local bytes = stream:read(65536)
        if not bytes or #bytes == 0 then break end
        writer:write(bytes)
        size = size + #bytes
    end
    stream:close()
    writer:close()
    return writer:getHashHex():lower(), size
end
local function json(filename)
    local data = read(filename)
    if not data or #data > 16 * 1024 * 1024 then fail('Missing or oversized JSON: ' .. filename) end
    local result = MOAIJsonParser.decode(data)
    if type(result) ~= 'table' then fail('Invalid JSON: ' .. filename) end
    return result
end
function module.readConfiguration()
    if not MOAIFileSystem.checkFileExists('mods/loader.json') then return nil end
    local config = json('mods/loader.json')
    if config.format ~= 'eastward-loader' or config.version ~= 1 or type(config.mods) ~= 'table' then fail('Unsupported loader configuration') end
    local ids = {}
    for _, mod in ipairs(config.mods) do
        if type(mod.id) ~= 'string' or not mod.id:match('^[%w_-]+$') or ids[mod.id] then fail('Invalid or duplicate mod ID') end
        ids[mod.id] = true
        path(mod.file)
        if type(mod.enabled) ~= 'boolean' or (mod.kind ~= 'ewmod' and mod.kind ~= 'lua') then fail('Invalid mod configuration') end
    end
    return config
end

local function collect(directory, result, prefix)
    for _, file in ipairs(MOAIFileSystem.listFiles(directory) or {}) do
        local relative = prefix .. file
        path(relative)
        if result[relative] then fail('Duplicate ZIP file: ' .. relative) end
        result[relative] = true
    end
    for _, child in ipairs(MOAIFileSystem.listDirectories(directory) or {}) do
        child = child:gsub('/$', '')
        path(child)
        collect(directory .. '/' .. child, result, prefix .. child .. '/')
    end
end
function module.activate(config)
    if module.active then fail('Resource packages are already active') end
    local enabled = {}
    for _, mod in ipairs(config.mods) do if mod.enabled and mod.kind == 'ewmod' then enabled[#enabled + 1] = mod end end
    local plan = config.deployment
    if #enabled == 0 then
        if plan and #((plan.packages) or {}) ~= 0 then fail('Deployment does not match enabled packages') end
        return
    end
    if type(plan) ~= 'table' or type(plan.packages) ~= 'table' or type(plan.resources) ~= 'table' then fail('Prepared deployment is missing') end
    if plan.mode ~= nil and plan.mode ~= 'ewmod' then fail('Unsupported deployment mode') end
    local packages, expected, actual, mappings = {}, {}, {}, {}
    for _, item in ipairs(plan.packages) do
        if packages[item.id] then fail('Duplicate prepared package') end
        packages[item.id] = hash(item.sha256)
    end
    local count = 0; for _ in pairs(packages) do count = count + 1 end
    if count ~= #enabled then fail('Deployment package count differs') end
    for _, resource in ipairs(plan.resources) do
        path(resource.path)
        if expected[resource.path] then fail('Duplicate prepared resource') end
        resource.hash = hash(resource.hash)
        resource.originalHash = nullable(resource.originalHash)
        if resource.originalHash ~= nil then resource.originalHash = hash(resource.originalHash) end
        expected[resource.path] = resource
    end
    local mounted = {}
    local ok, reason = pcall(function()
        for _, mod in ipairs(enabled) do
            local filename = 'mods/' .. path(mod.file)
            local packageHash = digest(filename)
            if packageHash ~= packages[mod.id] then fail('Package changed since preparation: ' .. mod.id) end
            local root = '__eastward_modloader_packages/' .. mod.id
            if not MOAIFileSystem.mountVirtualDirectory(root, filename) then fail('ZIP VFS mount failed: ' .. mod.id) end
            mounted[#mounted + 1] = root
            local manifest = json(root .. '/manifest.json')
            if manifest.format ~= 'eastward-mod' or manifest.version ~= 1 or type(manifest.resources) ~= 'table' then fail('Unsupported package: ' .. mod.id) end
            local files = {}; collect(root, files, '')
            if not files['manifest.json'] then fail('Missing package manifest') end
            files['manifest.json'] = nil
            local resources = 0
            for resourcePath, metadata in pairs(manifest.resources) do
                path(resourcePath)
                if not resourcePath:find('/', 1, true) or resourcePath:match('^_system/') or resourcePath:match('^mods/') or resourcePath:match('^lib/') or resourcePath:match('^__eastward_modloader_packages/') or (resourcePath == 'config/game_config' or resourcePath == 'config/game_config.json') then fail('Reserved resource path: ' .. resourcePath) end
                if type(metadata) ~= 'table' or type(metadata.size) ~= 'number' or metadata.size < 0 or metadata.size % 1 ~= 0 then fail('Invalid resource size') end
                metadata.originalHash = nullable(metadata.originalHash)
                local wanted = expected[resourcePath]
                if not wanted or wanted.size ~= metadata.size or wanted.hash ~= hash(metadata.hash)
                    or wanted.originalHash ~= (metadata.originalHash and hash(metadata.originalHash)) then fail('Prepared resource differs: ' .. resourcePath) end
                local member = 'resources/' .. resourcePath
                if not files[member] then fail('Missing ZIP resource: ' .. member) end
                files[member] = nil
                local checksum, size = digest(root .. '/' .. member)
                if checksum ~= wanted.hash or size ~= wanted.size then fail('Corrupt resource: ' .. resourcePath) end
                if not actual[resourcePath] then
                    local exists = MOAIFileSystem.checkFileExists(resourcePath)
                    if wanted.originalHash then
                        if not exists or digest(resourcePath) ~= wanted.originalHash then fail('Original resource differs: ' .. resourcePath) end
                    elseif exists then fail('New resource already exists: ' .. resourcePath) end
                end
                mappings[resourcePath] = mappings[resourcePath] or (root .. '/' .. member)
                actual[resourcePath] = true
                resources = resources + 1
            end
            if resources == 0 or next(files) then fail('ZIP entries do not match manifest: ' .. mod.id) end
        end
        for resource in pairs(expected) do if not actual[resource] then fail('Prepared resource has no enabled package: ' .. resource) end end
        local source = read('_system/__eastward_modloader/overlay.lua')
        if not source then fail('Direct resource reader missing; update the loader') end
        local chunk, error = loadstring(source, '@EastwardModLoader.overlay')
        if not chunk then fail(error) end
        module.overlay = chunk().install(mappings)
    end)
    if not ok then
        for _, root in ipairs(mounted) do MOAIFileSystem.mountVirtualDirectory(root) end
        error(reason, 0)
    end
    module.active = true
    module.mounts = mounted
end
return module
