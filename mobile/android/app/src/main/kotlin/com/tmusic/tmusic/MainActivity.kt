package com.tmusic.tmusic

import android.content.Intent
import com.ryanheise.audioservice.AudioServiceActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : AudioServiceActivity() {
    private var channel: MethodChannel? = null
    private val pending = mutableListOf<String>()
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        channel = MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "tmusic/shared_links")
        channel?.setMethodCallHandler { call, result ->
            if (call.method == "takePending") {
                result.success(pending.toList())
                pending.clear()
            } else result.notImplemented()
        }
        receive(intent)
    }
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        receive(intent)
        channel?.invokeMethod("available", null)
    }
    private fun receive(incoming: Intent?) {
        if (incoming?.action != Intent.ACTION_SEND && incoming?.action != Intent.ACTION_SEND_MULTIPLE) return
        val text = incoming.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()
        if (text != null && text.length <= 16384 && !pending.contains(text)) pending.add(text)
        incoming.clipData?.let { data ->
            for (index in 0 until minOf(data.itemCount, 8)) {
                val value = data.getItemAt(index).text?.toString() ?: continue
                if (value.length <= 16384 && !pending.contains(value)) pending.add(value)
            }
        }
        incoming.action = Intent.ACTION_MAIN
        incoming.removeExtra(Intent.EXTRA_TEXT)
    }
}
