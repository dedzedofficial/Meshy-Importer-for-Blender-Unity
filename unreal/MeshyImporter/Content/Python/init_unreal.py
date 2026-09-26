"""Runs automatically when the Unreal Editor starts with the Meshy Importer plugin enabled."""

import unreal

try:
    import meshy_unreal

    meshy_unreal.on_editor_start()
except Exception as exc:  # never break editor start-up
    unreal.log_error("Meshy Importer: failed to start: %s" % exc)
