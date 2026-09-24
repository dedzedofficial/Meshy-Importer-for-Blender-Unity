"""Runs automatically when the Unreal Editor starts with the Meshy Importer plugin enabled."""

import unreal

try:
    import meshy_unreal

    meshy_unreal.register_menus()
except Exception as exc:  # never break editor start-up
    unreal.log_error("Meshy Importer: failed to register menus: %s" % exc)
