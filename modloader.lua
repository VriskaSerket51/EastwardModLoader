function log(msg)
    mock._logWithToken("MOD_LOADER", msg)
end

function beforeGameLoad()
end

function onGameInit()
    log("Hello, Eastward!")

    local debugUIManager = mock.getDebugUIManager()

    debugUIManager:init()
    debugUIManager:setEnabled(false)
    mock.getLogViewManager():init()
    game:setDebugUIEnabled(true)
end

function onSceneInit()
    -- log("onSceneInit")
end

function onSceneStart()
    -- log("onSceneStart")
end

function onSceneOpen(scene)
    import("script.Common")

    local path = scene.path
    local main = scene.main
    log(path .. ": " .. tostring(main))

    if (path == "scene/design/ui/F_HUD.scene") then
        mock.addLogOnScreen("Hello, John and Sam!")
    elseif (path == "scene/design/ui/Logo.scene") then
        game:openSceneByPath("scene/ui/TitleMenu.scene")
    end
end

function onMenuOpen()
    import("script.Common")
end

function init()
    registerGlobalSignals({
        "ew.setting.changed",
        "ew.resolution.changed",
        "ew.global_flag.changed",
        "ew.loading.done",
        "ew.cutscene.on",
        "ew.cutscene.off",
        "ew.cooking.start",
        "ew.cooking.stop",
        "ew.fullbright.on",
        "ew.fullbright.off",
        "ew.hero.spawn",
        "ew.hero.remove",
        "ew.hero.ready",
        "ew.prototype_test.start",
        "ew.prototype_test.stop",
        "ew.dialog.start",
        "ew.dialog.stop",
        "ew.dialog.abort_all",
        "ew.npc.command",
        "ew.menu.open",
        "ew.menu.close",
        "ew.cinema_mask.show",
        "ew.cinema_mask.hide",
        "ew.world_menu.open",
        "ew.world_menu.close",
        "ew.fastmenu.open",
        "ew.fastmenu.close",
        "ew.item.get",
        "ew.item.lose",
        "ew.item.use",
        "ew.gacha.get",
        "ew.game.pause",
        "ew.game.resume",
        "ew.game.over",
        "ew.game.save",
        "ew.game.autosaving",
        "ew.game.autosaved",
        "ew.game.autosave_retain",
        "ew.globalroutine.enter",
        "ew.globalroutine.exit",
        "ew.enemy.pause",
        "ew.enemy.resume",
        "ew.minigame.start",
        "ew.minigame.stop",
        "ew.minigame.reset",
        "ew.minigame.close",
        "ew.minigame.pause",
        "ew.minigame.resume",
        "ew.minigame.win",
        "ew.minigame.game_over",
        "ew.chest.open",
        "ew.ach_stat.change",
        "ew.visualconfig.change",
        "ew.humanfactory.command",
        "ew.raft.update",
        "ew.story.chapter_end",
        "ew.story.chapter_start",
        "ew.quest_log.update",
        "ew.sq.talk",
        "ew.screenshot.start",
        "ew.screenshot.stop",
        "ew.joystick.change",
        "ew.newcontent.add",
        "ew.newcontent.check"
    })
end

function connect()
    mock.connectGlobalSignalFunc("game.init", onGameInit)
    mock.connectGlobalSignalFunc("scene.start", onSceneStart)
    mock.connectGlobalSignalFunc("scene.init", onSceneInit)
    mock.connectGlobalSignalFunc("scene.open", onSceneOpen)
    mock.connectGlobalSignalFunc("ew.menu.open", onMenuOpen)
end

init()
beforeGameLoad()
connect()
