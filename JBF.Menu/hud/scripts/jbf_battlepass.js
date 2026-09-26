"use strict";

(function () {
    function cmd(value) {
        if (typeof GameInterfaceAPI !== "undefined" && GameInterfaceAPI.ConsoleCommand) {
            GameInterfaceAPI.ConsoleCommand(value);
        }
    }

    $.GetContextPanel().JBF_BattlePass = {
        Tab: function (name) { cmd("css_bp_tab " + name); },
        Page: function (delta) { cmd("css_bp_page " + delta); },
        Claim: function (slot) { cmd("css_bp_claim_slot " + slot); },
        Use: function (slot) { cmd("css_bp_use_slot " + slot); },
        Close: function () { cmd("css_bp_close"); }
    };

    // Expose a stable global name for XML onactivate handlers.
    JBF_BattlePass = $.GetContextPanel().JBF_BattlePass;
})();
