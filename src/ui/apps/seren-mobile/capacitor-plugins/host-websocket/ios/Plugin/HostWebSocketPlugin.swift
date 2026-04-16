import Capacitor
import Foundation

/// Capacitor plugin exposing a native WebSocket bridge backed by
/// `URLSessionWebSocketTask`. The JS side addresses each logical connection
/// by an instanceId so multiple sockets can coexist.
@objc(HostWebSocketPlugin)
public class HostWebSocketPlugin: CAPPlugin {
    private var tasks: [String: URLSessionWebSocketTask] = [:]
    private let queue = DispatchQueue(label: "com.seren.hostwebsocket.queue")

    @objc func connect(_ call: CAPPluginCall) {
        guard let instanceId = call.getString("instanceId"),
              let urlString = call.getString("url"),
              let url = URL(string: urlString) else {
            call.reject("connect: missing instanceId or url")
            return
        }

        queue.async {
            if self.tasks[instanceId] != nil {
                call.reject("connect: instance \(instanceId) already connected")
                return
            }

            var request = URLRequest(url: url)
            if let headers = call.getObject("headers") as? [String: String] {
                for (key, value) in headers {
                    request.addValue(value, forHTTPHeaderField: key)
                }
            }

            let session = URLSession(configuration: .default)
            let task = session.webSocketTask(with: request)
            self.tasks[instanceId] = task

            task.resume()
            self.notifyListeners("onOpen", data: ["instanceId": instanceId])
            self.listen(instanceId: instanceId, task: task)

            call.resolve()
        }
    }

    @objc func send(_ call: CAPPluginCall) {
        guard let instanceId = call.getString("instanceId"),
              let data = call.getString("data") else {
            call.reject("send: missing instanceId or data")
            return
        }

        guard let task = tasks[instanceId] else {
            call.reject("send: unknown instance \(instanceId)")
            return
        }

        task.send(.string(data)) { error in
            if let error = error {
                call.reject("send failed: \(error.localizedDescription)")
            } else {
                call.resolve()
            }
        }
    }

    @objc func sendBinary(_ call: CAPPluginCall) {
        guard let instanceId = call.getString("instanceId"),
              let base64 = call.getString("dataBase64"),
              let data = Data(base64Encoded: base64) else {
            call.reject("sendBinary: missing or invalid fields")
            return
        }

        guard let task = tasks[instanceId] else {
            call.reject("sendBinary: unknown instance \(instanceId)")
            return
        }

        task.send(.data(data)) { error in
            if let error = error {
                call.reject("sendBinary failed: \(error.localizedDescription)")
            } else {
                call.resolve()
            }
        }
    }

    @objc func close(_ call: CAPPluginCall) {
        guard let instanceId = call.getString("instanceId") else {
            call.reject("close: missing instanceId")
            return
        }

        let code = call.getInt("code") ?? 1000
        let reason = call.getString("reason") ?? ""

        queue.async {
            guard let task = self.tasks[instanceId] else {
                call.resolve()
                return
            }

            let closeCode = URLSessionWebSocketTask.CloseCode(rawValue: code) ?? .normalClosure
            task.cancel(with: closeCode, reason: reason.data(using: .utf8))
            self.tasks.removeValue(forKey: instanceId)

            self.notifyListeners("onClose", data: [
                "instanceId": instanceId,
                "code": code,
                "reason": reason,
                "wasClean": true
            ])

            call.resolve()
        }
    }

    private func listen(instanceId: String, task: URLSessionWebSocketTask) {
        task.receive { [weak self] result in
            guard let self = self else { return }

            switch result {
            case .failure(let error):
                self.notifyListeners("onError", data: [
                    "instanceId": instanceId,
                    "message": error.localizedDescription
                ])
                self.queue.async {
                    self.tasks.removeValue(forKey: instanceId)
                }

            case .success(let message):
                switch message {
                case .string(let text):
                    self.notifyListeners("onMessage", data: [
                        "instanceId": instanceId,
                        "data": text
                    ])
                case .data(let data):
                    self.notifyListeners("onMessage", data: [
                        "instanceId": instanceId,
                        "dataBase64": data.base64EncodedString()
                    ])
                @unknown default:
                    break
                }

                if self.tasks[instanceId] != nil {
                    self.listen(instanceId: instanceId, task: task)
                }
            }
        }
    }
}
