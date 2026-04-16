package com.seren.plugins.hostwebsocket

import android.util.Base64
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

/**
 * Capacitor plugin exposing a native WebSocket bridge backed by OkHttp.
 * Multiple independent sockets are tracked by their JS-provided instanceId.
 */
@CapacitorPlugin(name = "HostWebSocket")
class HostWebSocketPlugin : Plugin() {

    private val sockets = ConcurrentHashMap<String, WebSocket>()

    private val client: OkHttpClient by lazy {
        OkHttpClient.Builder()
            .connectTimeout(10, TimeUnit.SECONDS)
            .readTimeout(0, TimeUnit.MILLISECONDS)
            .pingInterval(0, TimeUnit.SECONDS)
            .retryOnConnectionFailure(false)
            .build()
    }

    @PluginMethod
    fun connect(call: PluginCall) {
        val instanceId = call.getString("instanceId") ?: run {
            call.reject("connect: missing instanceId"); return
        }
        val url = call.getString("url") ?: run {
            call.reject("connect: missing url"); return
        }

        if (sockets.containsKey(instanceId)) {
            call.reject("connect: instance $instanceId already connected")
            return
        }

        val requestBuilder = Request.Builder().url(url)
        val headers = call.getObject("headers")
        if (headers != null) {
            val keys = headers.keys()
            while (keys.hasNext()) {
                val key = keys.next()
                requestBuilder.addHeader(key, headers.getString(key) ?: "")
            }
        }

        val listener = object : WebSocketListener() {
            override fun onOpen(ws: WebSocket, response: Response) {
                val payload = JSObject().put("instanceId", instanceId)
                notifyListeners("onOpen", payload)
            }

            override fun onMessage(ws: WebSocket, text: String) {
                val payload = JSObject()
                    .put("instanceId", instanceId)
                    .put("data", text)
                notifyListeners("onMessage", payload)
            }

            override fun onMessage(ws: WebSocket, bytes: ByteString) {
                val payload = JSObject()
                    .put("instanceId", instanceId)
                    .put("dataBase64", Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP))
                notifyListeners("onMessage", payload)
            }

            override fun onClosed(ws: WebSocket, code: Int, reason: String) {
                val payload = JSObject()
                    .put("instanceId", instanceId)
                    .put("code", code)
                    .put("reason", reason)
                    .put("wasClean", true)
                notifyListeners("onClose", payload)
                sockets.remove(instanceId)
            }

            override fun onFailure(ws: WebSocket, t: Throwable, response: Response?) {
                val payload = JSObject()
                    .put("instanceId", instanceId)
                    .put("message", t.message ?: "unknown error")
                notifyListeners("onError", payload)
                sockets.remove(instanceId)
            }
        }

        val socket = client.newWebSocket(requestBuilder.build(), listener)
        sockets[instanceId] = socket
        call.resolve()
    }

    @PluginMethod
    fun send(call: PluginCall) {
        val instanceId = call.getString("instanceId") ?: run {
            call.reject("send: missing instanceId"); return
        }
        val data = call.getString("data") ?: run {
            call.reject("send: missing data"); return
        }

        val socket = sockets[instanceId] ?: run {
            call.reject("send: unknown instance $instanceId"); return
        }

        if (socket.send(data)) call.resolve()
        else call.reject("send: enqueue failed (buffer full or closed)")
    }

    @PluginMethod
    fun sendBinary(call: PluginCall) {
        val instanceId = call.getString("instanceId") ?: run {
            call.reject("sendBinary: missing instanceId"); return
        }
        val base64 = call.getString("dataBase64") ?: run {
            call.reject("sendBinary: missing dataBase64"); return
        }

        val socket = sockets[instanceId] ?: run {
            call.reject("sendBinary: unknown instance $instanceId"); return
        }

        val bytes = Base64.decode(base64, Base64.DEFAULT)
        if (socket.send(ByteString.of(*bytes))) call.resolve()
        else call.reject("sendBinary: enqueue failed")
    }

    @PluginMethod
    fun close(call: PluginCall) {
        val instanceId = call.getString("instanceId") ?: run {
            call.reject("close: missing instanceId"); return
        }
        val code = call.getInt("code") ?: 1000
        val reason = call.getString("reason") ?: ""

        val socket = sockets[instanceId]
        if (socket != null) {
            socket.close(code, reason)
            sockets.remove(instanceId)
        }
        call.resolve()
    }
}
