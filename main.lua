--------------------------------------------------------------------
--Env
--------------------------------------------------------------------
local SCRIPT_EXT            = rawget(_G, 'SCRIPT_EXT') or '.lua'
MOAI_LOG_LEVEL              = 'info'

GII_PROJECT_SCRIPT_LIB_PATH = 'lib'
GII_PROJECT_ASSET_PATH      = 'asset'

package.path                = ''
	.. (GII_PROJECT_SCRIPT_LIB_PATH .. '/?' .. SCRIPT_EXT .. ';')
	.. (GII_PROJECT_SCRIPT_LIB_PATH .. '/?/init' .. SCRIPT_EXT .. ';')
	.. package.path

--------------------------------------------------------------------
--Runtimes
--------------------------------------------------------------------
if MOAIEnvironment.documentDirectory then
	MOAIFileSystem.setWorkingDirectory(MOAIEnvironment.documentDirectory .. '/game')
end


require 'gamelib'

if MOAIFileSystem.checkFileExists('EASTWARD_DEBUG') then
	-- if MOAIEnvironment.DEBUG_ENABLED == '1' then
	mock.__nodebug = false
else
	mock.__nodebug = true
end

-- mock.setLogLevel( 'status' )
-- MOCK_ASYNC_RENDER_MODE = false
mock.TEXTURE_ASYNC_LOAD = true

MOCK_DEFAULT_MOUSE_MODE = 'relative'

MOCK_PRE_GAME_INIT = function()
	game:setUserObject('FullGame', true)
	game:setUserObject('manual_preload', true)

	if mock.__nodebug then
		game:setConfig('socket_command_enabled', false)
		game:setConfig('http_server_enabled', false)
		-- game:setConfig( 'gii_host_ip', '192.168.33.171' )
		game:setConfig('gii_sync_enabled', false)
	else
		game:setConfig('socket_command_enabled', true)
		game:setConfig('http_server_enabled', true)
		game:setConfig('gii_host_ip', '192.168.33.171')
	end

	MOCK_DEFAULT_LOCALE = 'en'
	MOCK_FORCE_SOURCE_LOCALE = false

	if MOAIEnvironment.CRT_FX_ENABLED == '1' then
		game:setUserObject('output_crt_effect', true)
	end

	if MOAIEnvironment.LOG_ENABLED == '1' then
		mock.setLogLevel('status')
	end
end

MOCK_ON_GAME_INIT = function()
	game:setUserObject('relative_mouse_enabled', true)
	local debugging = not mock.__nodebug
	game:setUserObject('obs_annoation_enabled', debugging)
	game:setUserObject('pause_on_focus_lost', debugging)
end

--------------------------------------------------------------------
--Startup
--------------------------------------------------------------------
local function injectModLoader()
	MOAILogMgr.openFile("ModLoader.log")

	MOCKHelper.log("MISC", "--------------------------------------------------------------------")
	MOCKHelper.log("MISC", "Logging started:\t" .. os.date("%Y-%m-%d %H:%M:%S"))
	MOCKHelper.log("MISC", "--------------------------------------------------------------------")

	function string:endswith(ending)
		return ending == "" or self:sub(- #ending) == ending
	end

	function log(msg)
		mock._logWithToken("MOD_LOADER", msg)
	end

	for i, f in ipairs(MOAIFileSystem.listFiles("mods")) do
		if f:endswith(".lua") then
			local fName = f:sub(1, #f - 4)
			GameModule.addGameModuleMapping(fName, "mods/" .. f, "mods/" .. f)
			GameModule.loadGameModule(fName)
			local errors = GameModule.getErrorInfo()
			if errors then
				log("Errors in loading mod: " .. fName)
				log("------------------------------")

				for i, info in ipairs(errors) do
					if info.errtype == "compile" then
						log("error in compiling " .. info.fullpath)
					elseif info.errtype == "load" then
						log("error in loading " .. info.fullpath)
					end

					log(info.msg)
					log()
				end

				log("------------------------------")
			end
		end
	end

	MOAILogMgr.closeFile()
end

mock._stat = function(...)
	mock._logWithToken("STAT :mock", ...)
end

mock.setupEnvironment(
	false,
	'config/game'
)
injectModLoader()
mock.init('config/game_config')
game:getGlobalManager('LogViewManager'):setEnabled(false)
mock.start()
