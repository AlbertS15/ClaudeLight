// ClaudeLight — Spotlight-style launcher: finds apps and files, and asks Claude.
// Option+Space opens the panel. Built by build.sh with swiftc, no Xcode project.

import AppKit
import Carbon.HIToolbox
import Combine
import ServiceManagement
import SwiftUI

// MARK: - Search results

struct Hit: Identifiable, Equatable {
    let id: String
    let name: String
    let path: String
    let isApp: Bool

    var url: URL { URL(fileURLWithPath: path) }
    var subtitle: String {
        let dir = (path as NSString).deletingLastPathComponent
        return dir.replacingOccurrences(of: NSHomeDirectory(), with: "~")
    }
}

/// Wraps NSMetadataQuery, the same index Spotlight reads.
final class FileSearch: NSObject {
    private var query: NSMetadataQuery?
    private var pending: DispatchWorkItem?
    private var term = ""
    var onResults: ([Hit]) -> Void = { _ in }

    func search(_ text: String) {
        pending?.cancel()
        stop()
        let term = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !term.isEmpty else {
            onResults([])
            return
        }
        let work = DispatchWorkItem { [weak self] in self?.start(term) }
        pending = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.12, execute: work)
    }

    private func start(_ term: String) {
        self.term = term.lowercased()
        let escaped = term.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        let string = "kMDItemDisplayName == \"*\(escaped)*\"cd || kMDItemTextContent == \"\(escaped)*\"cdw"
        guard let predicate = NSPredicate(fromMetadataQueryString: string) else { return }

        let q = NSMetadataQuery()
        q.predicate = predicate
        q.searchScopes = [NSMetadataQueryLocalComputerScope]
        q.sortDescriptors = [NSSortDescriptor(key: "kMDItemLastUsedDate", ascending: false)]
        NotificationCenter.default.addObserver(self, selector: #selector(update), name: .NSMetadataQueryDidUpdate, object: q)
        NotificationCenter.default.addObserver(self, selector: #selector(finish), name: .NSMetadataQueryDidFinishGathering, object: q)
        query = q
        q.start()
    }

    @objc private func update() { publish() }

    @objc private func finish() {
        publish()
        query?.stop()
    }

    private func publish() {
        guard let q = query else { return }
        q.disableUpdates()
        var apps: [Hit] = []
        var files: [Hit] = []
        var seen = Set<String>()
        for i in 0..<min(q.resultCount, 400) {
            guard let item = q.result(at: i) as? NSMetadataItem,
                  let path = item.value(forAttribute: NSMetadataItemPathKey) as? String,
                  !seen.contains(path),
                  !Self.isNoise(path)
            else { continue }
            seen.insert(path)
            let name = (item.value(forAttribute: NSMetadataItemDisplayNameKey) as? String) ?? (path as NSString).lastPathComponent
            let type = item.value(forAttribute: "kMDItemContentType") as? String
            let isApp = type == "com.apple.application-bundle"
            let hit = Hit(id: path, name: isApp ? name.replacingOccurrences(of: ".app", with: "") : name, path: path, isApp: isApp)
            if isApp { apps.append(hit) } else { files.append(hit) }
        }
        q.enableUpdates()
        // Name matches first, as Spotlight does; the sort is stable, so recency holds within each group.
        let named = { (h: Hit) in h.name.lowercased().contains(self.term) }
        files = files.filter(named) + files.filter { !named($0) }
        onResults(Array(apps.prefix(5)) + Array(files.prefix(12 - min(apps.count, 5))))
    }

    /// System and app-internal files Spotlight keeps out of its own results.
    static func isNoise(_ path: String) -> Bool {
        if path.hasSuffix(".app") && (path.hasPrefix("/Applications/") || path.hasPrefix("/System/Applications/")) {
            return false
        }
        let blocked = ["/Library/", "/usr/", "/System/", "/private/", "/opt/", "/.Trash/", "/node_modules/", ".app/Contents/"]
        if blocked.contains(where: { path.contains($0) }) { return true }
        return path.split(separator: "/").contains { $0.hasPrefix(".") }
    }

    func stop() {
        if let q = query {
            q.stop()
            NotificationCenter.default.removeObserver(self, name: nil, object: q)
        }
        query = nil
    }
}

// MARK: - Settings

/// The models the picker offers; `auto` leaves the choice to Claude Code's own default.
enum ClaudeModel: String, CaseIterable, Identifiable {
    case auto = "", haiku, sonnet, opus, fable

    var id: String { rawValue }

    var title: String {
        switch self {
        case .auto: return S.modelAuto
        case .haiku: return "Haiku"
        case .sonnet: return "Sonnet"
        case .opus: return "Opus"
        case .fable: return "Fable"
        }
    }

    var note: String {
        switch self {
        case .auto: return S.noteAuto
        case .haiku: return S.noteHaiku
        case .sonnet: return S.noteSonnet
        case .opus: return S.noteOpus
        case .fable: return S.noteFable
        }
    }
}

/// A model on any OpenAI-compatible service. The API key lives in the Keychain under `id`.
struct Connection: Codable, Identifiable, Equatable {
    var id = UUID()
    var name: String
    var baseURL: String
    var model: String
}

/// Ready-made addresses for the add-connection form.
enum ServicePreset: String, CaseIterable, Identifiable {
    case openRouter, openAI, deepSeek, groq, mistral, yandex, gigaChat, ollama, lmStudio, other

    var id: String { rawValue }

    var title: String {
        switch self {
        case .openRouter: return "OpenRouter"
        case .openAI: return "OpenAI"
        case .deepSeek: return "DeepSeek"
        case .groq: return "Groq"
        case .mistral: return "Mistral"
        case .yandex: return "YandexGPT"
        case .gigaChat: return "GigaChat"
        case .ollama: return S.presetOllama
        case .lmStudio: return S.presetLmstudio
        case .other: return S.presetOther
        }
    }

    var baseURL: String {
        switch self {
        case .openRouter: return "https://openrouter.ai/api/v1"
        case .openAI: return "https://api.openai.com/v1"
        case .deepSeek: return "https://api.deepseek.com"
        case .groq: return "https://api.groq.com/openai/v1"
        case .mistral: return "https://api.mistral.ai/v1"
        case .yandex: return "https://ai.api.cloud.yandex.net/v1"
        case .gigaChat: return "https://api.giga.chat/v1"
        case .ollama: return "http://localhost:11434/v1"
        case .lmStudio: return "http://localhost:1234/v1"
        case .other: return ""
        }
    }

    var modelHint: String {
        switch self {
        case .openRouter: return S.hintExample("openai/gpt-4o")
        case .openAI: return S.hintExample("gpt-4o")
        case .deepSeek: return S.hintExample("deepseek-flash")
        case .groq: return S.hintExample("llama-3.3-70b-versatile")
        case .mistral: return S.hintExample("mistral-large-latest")
        case .yandex: return S.hintExample("gpt://b1g…/yandexgpt")
        case .gigaChat: return S.hintExample("GigaChat-2")
        case .ollama: return S.hintExample("llama3.2")
        case .lmStudio: return S.hintLoadedModel
        case .other: return S.hintModelName
        }
    }

    var needsKey: Bool { self != .ollama && self != .lmStudio }
}

/// Generic-password items in the login Keychain, one per connection.
enum Keychain {
    private static let service = "io.github.alberts15.claudelight"

    static func set(_ key: String, for id: UUID) {
        delete(id)
        guard !key.isEmpty else { return }
        let item: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString,
            kSecValueData as String: Data(key.utf8),
        ]
        SecItemAdd(item as CFDictionary, nil)
    }

    static func get(_ id: UUID) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString,
            kSecReturnData as String: true,
        ]
        var out: AnyObject?
        guard SecItemCopyMatching(query as CFDictionary, &out) == errSecSuccess, let data = out as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func delete(_ id: UUID) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: id.uuidString,
        ]
        SecItemDelete(query as CFDictionary)
    }
}

/// What answers a question: a Claude model through Claude Code, or a connection.
/// Gemini models through the person's own Google login in Gemini CLI; `auto` leaves the choice to the CLI.
enum GeminiModel: String, CaseIterable, Identifiable {
    case auto = "", pro = "gemini-3.1-pro-preview", flash = "gemini-3.8-flash", lite = "gemini-3.1-flash-lite"

    var id: String { rawValue }

    var title: String {
        switch self {
        case .auto: return "Gemini"
        case .pro: return "Gemini 3.1 Pro"
        case .flash: return "Gemini 3.8 Flash"
        case .lite: return "Gemini 3.1 Flash-Lite"
        }
    }

    var note: String {
        switch self {
        case .auto: return S.noteGeminiAuto
        case .pro: return S.noteOpus
        case .flash: return S.noteSonnet
        case .lite: return S.noteHaiku
        }
    }
}

enum Choice: Hashable {
    case claude(ClaudeModel)
    case gemini(GeminiModel)
    case codex
    case custom(UUID)

    var stored: String {
        switch self {
        case let .claude(m): return "claude:" + m.rawValue
        case let .gemini(m): return "gemini:" + m.rawValue
        case .codex: return "codex:"
        case let .custom(id): return "custom:" + id.uuidString
        }
    }

    init(stored: String) {
        if stored.hasPrefix("custom:"), let id = UUID(uuidString: String(stored.dropFirst(7))) {
            self = .custom(id)
        } else if stored.hasPrefix("codex:") {
            self = .codex
        } else if stored.hasPrefix("gemini:") {
            self = .gemini(GeminiModel(rawValue: String(stored.dropFirst(7))) ?? .auto)
        } else {
            let raw = stored.hasPrefix("claude:") ? String(stored.dropFirst(7)) : stored
            self = .claude(ClaudeModel(rawValue: raw) ?? .auto)
        }
    }
}

/// Choices kept across launches and shared by the panel, the welcome window and the menu.
final class Settings: ObservableObject {
    static let shared = Settings()

    @Published var choice: Choice {
        didSet { UserDefaults.standard.set(choice.stored, forKey: "model") }
    }
    /// Whether results offer "Ask in ChatGPT" (opens chatgpt.com on the person's own login).
    @Published var showsChatGPT: Bool {
        didSet { UserDefaults.standard.set(showsChatGPT, forKey: "showsChatGPT") }
    }
    /// Interface language code, "" for the system's. Strings read it through Lang.index.
    @Published var language: String {
        didSet { UserDefaults.standard.set(language, forKey: "language") }
    }
    @Published private(set) var connections: [Connection] {
        didSet { UserDefaults.standard.set(try? JSONEncoder().encode(connections), forKey: "connections") }
    }

    private init() {
        choice = Choice(stored: UserDefaults.standard.string(forKey: "model") ?? "")
        language = UserDefaults.standard.string(forKey: "language") ?? ""
        showsChatGPT = UserDefaults.standard.object(forKey: "showsChatGPT") as? Bool ?? true
        let data = UserDefaults.standard.data(forKey: "connections")
        connections = data.flatMap { try? JSONDecoder().decode([Connection].self, from: $0) } ?? []
        if case let .custom(id) = choice, !connections.contains(where: { $0.id == id }) { choice = .claude(.auto) }
    }

    var connection: Connection? {
        guard case let .custom(id) = choice else { return nil }
        return connections.first { $0.id == id }
    }

    func title(of c: Choice) -> String {
        switch c {
        case let .claude(m): return m.title
        case let .gemini(m): return m.title
        case .codex: return S.codexTitle
        case let .custom(id): return connections.first { $0.id == id }?.name ?? "—"
        }
    }

    func add(_ connection: Connection, key: String) {
        Keychain.set(key, for: connection.id)
        connections.append(connection)
        choice = .custom(connection.id)
    }

    func remove(_ connection: Connection) {
        Keychain.delete(connection.id)
        connections.removeAll { $0.id == connection.id }
        if choice == .custom(connection.id) { choice = .claude(.auto) }
    }
}

/// The label of the hotkey's key: Ё on a Russian layout, ` elsewhere (the same physical key).
var hotkeyKey: String { Lang.codes[Lang.index] == "ru" ? "Ё" : "`" }

/// Picks the interface language, "As in system" first.
struct LanguagePicker: View {
    @ObservedObject var settings = Settings.shared

    var body: some View {
        Picker("", selection: $settings.language) {
            Text(S.languageSystem).tag("")
            ForEach(Array(zip(Lang.codes, Lang.names)), id: \.0) { code, name in
                Text(name).tag(code)
            }
        }
        .labelsHidden()
        .fixedSize()
    }
}

extension Notification.Name {
    static let addConnection = Notification.Name("addConnection")
}

/// The model menu shown in the panel and the welcome window.
struct ModelPicker: View {
    @ObservedObject var settings = Settings.shared
    var isCompact = false

    var body: some View {
        Menu {
            Picker("Claude", selection: $settings.choice) {
                ForEach(ClaudeModel.allCases) { m in
                    Text("\(m.title) — \(m.note)").tag(Choice.claude(m))
                }
            }
            .pickerStyle(.inline)
            Picker(S.codexSection, selection: $settings.choice) {
                Text("\(S.codexTitle) — \(S.noteCodex)").tag(Choice.codex)
            }
            .pickerStyle(.inline)
            Picker(S.geminiSection, selection: $settings.choice) {
                ForEach(GeminiModel.allCases) { m in
                    Text("\(m.title) — \(m.note)").tag(Choice.gemini(m))
                }
            }
            .pickerStyle(.inline)
            if !settings.connections.isEmpty {
                Picker(S.connections, selection: $settings.choice) {
                    ForEach(settings.connections) { c in
                        Text("\(c.name) — \(c.model)").tag(Choice.custom(c.id))
                    }
                }
                .pickerStyle(.inline)
            }
            Divider()
            Button(S.connectModel) {
                NotificationCenter.default.post(name: .addConnection, object: nil)
            }
        } label: {
            Text(settings.title(of: settings.choice))
                .font(.system(size: isCompact ? 12 : 13, weight: .medium))
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
        .padding(.horizontal, isCompact ? 8 : 0)
        .padding(.vertical, isCompact ? 3 : 0)
        .background(
            Capsule().fill(Color.primary.opacity(isCompact ? 0.07 : 0))
        )
        .help(S.model)
    }
}

// MARK: - Other services

/// Streams a chat completion from an OpenAI-compatible endpoint, keeping the conversation for follow-ups.
final class APIRunner {
    private var task: Task<Void, Never>?
    private var history: [[String: String]] = []

    func reset() {
        cancel()
        history = []
    }

    func cancel() {
        task?.cancel()
        task = nil
    }

    func ask(_ question: String, via c: Connection, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        let base = c.baseURL.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard let url = URL(string: base + "/chat/completions") else {
            onDone(S.errBadUrl(c.name))
            return
        }
        // The question joins the history only with its answer, so a failed try never repeats in it.
        let messages = [["role": "system", "content": ClaudeRunner.systemPrompt]] + history + [["role": "user", "content": question]]

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.timeoutInterval = 120
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let key = Keychain.get(c.id) ?? ""
        if !key.isEmpty, !GigaChat.handles(url) {
            request.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        }
        // Yandex reads the folder from "gpt://<folder>/<model>"; the OpenAI SDK sends it as the project too.
        if c.model.hasPrefix("gpt://"), let folder = c.model.dropFirst(6).split(separator: "/").first {
            request.setValue(String(folder), forHTTPHeaderField: "OpenAI-Project")
        }
        request.httpBody = try? JSONSerialization.data(withJSONObject: [
            "model": c.model, "messages": messages, "stream": true,
        ] as [String: Any])

        task = Task { [weak self] in
            var reply = ""
            do {
                var request = request
                if GigaChat.handles(url) {
                    request.setValue("Bearer \(try await GigaChat.token(for: c.id, key: key))", forHTTPHeaderField: "Authorization")
                }
                let (bytes, response) = try await URLSession.shared.bytes(for: request)
                let status = (response as? HTTPURLResponse)?.statusCode ?? 0
                if status != 200 {
                    var body = ""
                    for try await line in bytes.lines { body += line }
                    throw APIError(status: status, body: body)
                }
                for try await line in bytes.lines {
                    guard line.hasPrefix("data:") else { continue }
                    let payload = line.dropFirst(5).trimmingCharacters(in: .whitespaces)
                    if payload == "[DONE]" { break }
                    guard let obj = try? JSONSerialization.jsonObject(with: Data(payload.utf8)) as? [String: Any] else { continue }
                    // Services such as OpenRouter report a failure after the 200 as an "error" event in the stream.
                    if let error = obj["error"] {
                        let detail = (error as? [String: Any])?["message"] as? String ?? "\(error)"
                        throw StreamError(message: S.errStream(c.name, detail))
                    }
                    guard let choices = obj["choices"] as? [[String: Any]],
                          let delta = choices.first?["delta"] as? [String: Any],
                          let text = delta["content"] as? String, !text.isEmpty
                    else { continue }
                    reply += text
                    await MainActor.run { onText(text) }
                }
                if reply.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    throw StreamError(message: S.errEmpty(c.name))
                }
                let full = reply
                await MainActor.run { [weak self] in
                    self?.history.append(["role": "user", "content": question])
                    self?.history.append(["role": "assistant", "content": full])
                    onDone(nil)
                }
            } catch is CancellationError {
                return
            } catch let e as StreamError {
                await MainActor.run { onDone(e.message) }
            } catch let e as APIError {
                await MainActor.run { onDone(e.message(for: c)) }
            } catch {
                if (error as? URLError)?.code == .cancelled { return }
                if let code = (error as? URLError)?.code,
                   [.serverCertificateUntrusted, .serverCertificateHasUnknownRoot, .serverCertificateHasBadDate,
                    .serverCertificateNotYetValid, .secureConnectionFailed].contains(code) {
                    await MainActor.run { onDone(S.errCert(c.name)) }
                    return
                }
                let hint = c.baseURL.contains("localhost") ? S.errLocalHint : ""
                await MainActor.run { onDone(S.errConnect(c.name, error.localizedDescription) + hint) }
            }
        }
    }
}

/// GigaChat trades the authorization key for a 30-minute access token before each chat; tokens are reused until they expire.
enum GigaChat {
    private static let authURL = URL(string: "https://ngw.devices.sberbank.ru:9443/api/v2/oauth")!
    private static var tokens: [UUID: (token: String, expires: Date)] = [:]
    private static let lock = NSLock()

    static func handles(_ url: URL) -> Bool {
        let host = url.host ?? ""
        return host.hasSuffix("giga.chat") || host.hasSuffix("sberbank.ru")
    }

    static func token(for id: UUID, key: String) async throws -> String {
        let cached = lock.withLock { tokens[id] }
        if let cached, cached.expires.timeIntervalSinceNow > 60 { return cached.token }

        var request = URLRequest(url: authURL)
        request.httpMethod = "POST"
        request.timeoutInterval = 30
        request.setValue("Basic \(key)", forHTTPHeaderField: "Authorization")
        request.setValue(UUID().uuidString.lowercased(), forHTTPHeaderField: "RqUID")
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.httpBody = Data("scope=GIGACHAT_API_PERS".utf8)

        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200,
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let token = json["access_token"] as? String
        else {
            // A refused key shows as a key error, like any other service.
            throw APIError(status: status == 200 ? 401 : status, body: String(data: data, encoding: .utf8) ?? "")
        }
        let expiresMs = (json["expires_at"] as? Double) ?? Date().addingTimeInterval(25 * 60).timeIntervalSince1970 * 1000
        lock.withLock { tokens[id] = (token, Date(timeIntervalSince1970: expiresMs / 1000)) }
        return token
    }
}

struct StreamError: Error {
    let message: String
}

struct APIError: Error {
    let status: Int
    let body: String

    static func isBlock(_ detail: String, _ body: String) -> Bool {
        let text = (detail + " " + body).lowercased()
        return ["security policy", "cloudflare", "blocked", "region", "country", "unsupported_country", "location is not supported"]
            .contains { text.contains($0) }
    }

    func message(for c: Connection) -> String {
        let json = try? JSONSerialization.jsonObject(with: Data(body.utf8)) as? [String: Any]
        let detail = (json?["error"] as? [String: Any])?["message"] as? String ?? (json?["error"] as? String) ?? String(body.prefix(300))
        switch status {
        // A 403 from a firewall (Cloudflare, a country block) arrives before any key check.
        case 403 where Self.isBlock(detail, body): return S.errBlocked(c.name, detail.contains("<") ? "Cloudflare" : detail)
        case 401, 403: return S.errKey(c.name, String(status), detail)
        case 404: return S.err404(c.name, detail)
        case 429: return S.err429(c.name, detail)
        default: return S.errStatus(c.name, String(status), detail)
        }
    }
}

// MARK: - Claude

/// Runs the local `claude` CLI in print mode and streams the reply text.
final class ClaudeRunner {
    private var process: Process?
    private(set) var sessionID: String?

    static let binary: String? = {
        let candidates = [
            NSHomeDirectory() + "/.local/bin/claude",
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
            NSHomeDirectory() + "/.claude/local/claude",
        ]
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }()

    static let systemPrompt = """
        You were called from a Spotlight-style quick bar on a Mac. Answer briefly and to the point, \
        in the language of the question. Use Markdown only for lists, bold and code.
        """

    func reset() {
        cancel()
        sessionID = nil
    }

    func cancel() {
        process?.terminate()
        process = nil
    }

    func ask(_ question: String, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        guard let binary = Self.binary else {
            onDone(S.errNoClaude)
            return
        }

        var args = [
            "-p", question,
            "--output-format", "stream-json", "--verbose", "--include-partial-messages",
            "--append-system-prompt", Self.systemPrompt,
            "--tools", "WebSearch,WebFetch",
            "--allowedTools", "WebSearch,WebFetch",
        ]
        if case let .claude(model) = Settings.shared.choice, model != .auto { args += ["--model", model.rawValue] }
        if let id = sessionID { args += ["--resume", id] }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: binary)
        p.arguments = args
        p.currentDirectoryURL = URL(fileURLWithPath: NSHomeDirectory())
        var env = ProcessInfo.processInfo.environment
        env["PATH"] = "\(NSHomeDirectory())/.local/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        p.environment = env

        let out = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        p.standardInput = FileHandle.nullDevice

        var buffer = Data()
        var streamed = false
        var error: String?
        let handle = out.fileHandleForReading

        handle.readabilityHandler = { [weak self] h in
            let chunk = h.availableData
            guard !chunk.isEmpty else {
                h.readabilityHandler = nil
                return
            }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl)
                buffer.removeSubrange(buffer.startIndex...nl)
                guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
                let type = obj["type"] as? String

                if type == "system", let id = obj["session_id"] as? String {
                    DispatchQueue.main.async { self?.sessionID = id }
                } else if type == "stream_event",
                          let event = obj["event"] as? [String: Any],
                          let delta = event["delta"] as? [String: Any],
                          delta["type"] as? String == "text_delta",
                          let text = delta["text"] as? String {
                    streamed = true
                    DispatchQueue.main.async { onText(text) }
                } else if type == "assistant",
                          let message = obj["message"] as? [String: Any],
                          let content = message["content"] as? [[String: Any]] {
                    let text = content.compactMap { $0["text"] as? String }.joined()
                    if obj["error"] != nil {
                        error = text
                    } else if !streamed, !text.isEmpty {
                        DispatchQueue.main.async { onText(text) }
                    } else if streamed, !text.isEmpty {
                        // Separates the text of successive assistant messages (around tool use).
                        DispatchQueue.main.async { onText("\n\n") }
                    }
                } else if type == "result", obj["is_error"] as? Bool == true, error == nil {
                    error = (obj["result"] as? String) ?? S.errClaude
                }
            }
        }

        p.terminationHandler = { [weak self] proc in
            DispatchQueue.main.async {
                if self?.process === proc { self?.process = nil }
                if proc.terminationReason == .uncaughtSignal { return }
                if let e = error, e.localizedCaseInsensitiveContains("authenticate") || e.localizedCaseInsensitiveContains("login") {
                    onDone(S.errNotLoggedIn)
                } else {
                    onDone(error)
                }
            }
        }

        do {
            try p.run()
            process = p
        } catch {
            onDone(S.errStart(error.localizedDescription))
        }
    }
}

// MARK: - Gemini

/// Runs Gemini CLI (`gemini`) headless on the person's own Google login and streams the reply.
/// Gemini CLI keeps no conversation between headless runs here, so follow-ups carry the history in the prompt.
final class GeminiRunner {
    private var process: Process?
    private var history: [(question: String, answer: String)] = []

    static var binary: String? {
        let home = NSHomeDirectory()
        var candidates = ["/opt/homebrew/bin/gemini", "/usr/local/bin/gemini", home + "/.local/bin/gemini", home + "/.npm-global/bin/gemini"]
        // nvm keeps one bin folder per Node version.
        if let versions = try? FileManager.default.contentsOfDirectory(atPath: home + "/.nvm/versions/node") {
            candidates += versions.sorted().reversed().map { home + "/.nvm/versions/node/\($0)/bin/gemini" }
        }
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    /// An empty folder to run in, so Gemini never reads or indexes the home folder.
    private static var workFolder: URL {
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ClaudeLight/gemini", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    func reset() {
        cancel()
        history = []
    }

    func cancel() {
        process?.terminate()
        process = nil
    }

    func ask(_ question: String, model: GeminiModel, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        guard let binary = Self.binary else {
            onDone(S.errNoGemini)
            return
        }

        // The conversation goes in on stdin; -p adds the instructions after it.
        var input = ""
        for turn in history {
            input += "User: \(turn.question)\n\nAssistant: \(turn.answer)\n\n"
        }
        input += history.isEmpty ? question : "User: \(question)"

        var args = ["-p", ClaudeRunner.systemPrompt, "--output-format", "stream-json", "--approval-mode", "plan"]
        if model != .auto { args += ["--model", model.rawValue] }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: binary)
        p.arguments = args
        p.currentDirectoryURL = Self.workFolder
        var env = ProcessInfo.processInfo.environment
        let nodeDir = (binary as NSString).deletingLastPathComponent
        env["PATH"] = "\(nodeDir):/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        p.environment = env

        let out = Pipe()
        let inPipe = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        p.standardInput = inPipe

        var buffer = Data()
        var reply = ""
        var error: String?
        out.fileHandleForReading.readabilityHandler = { h in
            let chunk = h.availableData
            guard !chunk.isEmpty else {
                h.readabilityHandler = nil
                return
            }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl)
                buffer.removeSubrange(buffer.startIndex...nl)
                guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
                switch obj["type"] as? String {
                case "message" where obj["role"] as? String == "assistant":
                    if let text = obj["content"] as? String, !text.isEmpty {
                        reply += text
                        DispatchQueue.main.async { onText(text) }
                    }
                case "error" where obj["severity"] as? String == "error":
                    error = obj["message"] as? String
                case "result" where obj["status"] as? String == "error":
                    error = ((obj["error"] as? [String: Any])?["message"] as? String) ?? error ?? S.errGemini("")
                default:
                    break
                }
            }
        }

        p.terminationHandler = { [weak self] proc in
            DispatchQueue.main.async {
                guard self?.process === proc else { return } // cancelled or replaced
                self?.process = nil
                let text = error ?? ""
                if ["auth", "login", "credential", "sign in"].contains(where: { text.localizedCaseInsensitiveContains($0) }) {
                    onDone(S.errGeminiLogin)
                } else if let e = error {
                    onDone(S.errGemini(e))
                } else if proc.terminationStatus != 0 && reply.isEmpty {
                    onDone(S.errGemini("exit \(proc.terminationStatus)"))
                } else {
                    self?.history.append((question, reply))
                    onDone(nil)
                }
            }
        }

        do {
            try p.run()
            process = p
            inPipe.fileHandleForWriting.write(Data(input.utf8))
            try? inPipe.fileHandleForWriting.close()
        } catch {
            onDone(S.errGemini(error.localizedDescription))
        }
    }
}

// MARK: - Codex

/// Runs Codex CLI (`codex exec`) read-only on the person's own ChatGPT login and returns its answer.
/// Codex sends the answer whole, not in pieces; follow-ups carry the conversation in the prompt.
final class CodexRunner {
    private var process: Process?
    private var history: [(question: String, answer: String)] = []

    static var binary: String? {
        let home = NSHomeDirectory()
        var candidates = ["/opt/homebrew/bin/codex", "/usr/local/bin/codex", home + "/.local/bin/codex", home + "/.npm-global/bin/codex"]
        if let versions = try? FileManager.default.contentsOfDirectory(atPath: home + "/.nvm/versions/node") {
            candidates += versions.sorted().reversed().map { home + "/.nvm/versions/node/\($0)/bin/codex" }
        }
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    /// An empty folder to run in, so Codex never reads the home folder.
    private static var workFolder: URL {
        let url = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ClaudeLight/codex", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    func reset() {
        cancel()
        history = []
    }

    func cancel() {
        process?.terminate()
        process = nil
    }

    func ask(_ question: String, onText: @escaping (String) -> Void, onDone: @escaping (String?) -> Void) {
        cancel()
        guard let binary = Self.binary else {
            onDone(S.errNoCodex)
            return
        }

        var input = ClaudeRunner.systemPrompt + " Do not run commands or read files; just answer.\n\n"
        for turn in history {
            input += "User: \(turn.question)\n\nAssistant: \(turn.answer)\n\n"
        }
        input += history.isEmpty ? question : "User: \(question)"

        let p = Process()
        p.executableURL = URL(fileURLWithPath: binary)
        p.arguments = ["exec", "--json", "--skip-git-repo-check", "--ephemeral",
                       "--sandbox", "read-only", "--cd", Self.workFolder.path, "-"]
        p.currentDirectoryURL = Self.workFolder
        var env = ProcessInfo.processInfo.environment
        env["PATH"] = "\((binary as NSString).deletingLastPathComponent):/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
        p.environment = env

        let out = Pipe()
        let inPipe = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        p.standardInput = inPipe

        var buffer = Data()
        var reply = ""
        var error: String?
        out.fileHandleForReading.readabilityHandler = { h in
            let chunk = h.availableData
            guard !chunk.isEmpty else {
                h.readabilityHandler = nil
                return
            }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl)
                buffer.removeSubrange(buffer.startIndex...nl)
                guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
                switch obj["type"] as? String {
                case "item.completed":
                    let item = obj["item"] as? [String: Any]
                    if item?["type"] as? String == "agent_message", let text = item?["text"] as? String, !text.isEmpty {
                        let piece = reply.isEmpty ? text : "\n\n" + text
                        reply += piece
                        DispatchQueue.main.async { onText(piece) }
                    }
                case "turn.failed":
                    error = ((obj["error"] as? [String: Any])?["message"] as? String) ?? error
                case "error":
                    error = (obj["message"] as? String) ?? error
                default:
                    break
                }
            }
        }

        p.terminationHandler = { [weak self] proc in
            DispatchQueue.main.async {
                guard self?.process === proc else { return } // cancelled or replaced
                self?.process = nil
                let text = error ?? ""
                if ["login", "logged in", "auth", "401", "unauthorized"].contains(where: { text.localizedCaseInsensitiveContains($0) }) {
                    onDone(S.errCodexLogin)
                } else if let e = error, reply.isEmpty {
                    onDone(S.errCodex(e))
                } else if proc.terminationStatus != 0 && reply.isEmpty {
                    onDone(S.errCodex("exit \(proc.terminationStatus)"))
                } else {
                    self?.history.append((question, reply))
                    onDone(nil)
                }
            }
        }

        do {
            try p.run()
            process = p
            inPipe.fileHandleForWriting.write(Data(input.utf8))
            try? inPipe.fileHandleForWriting.close()
        } catch {
            onDone(S.errCodex(error.localizedDescription))
        }
    }
}

// MARK: - Model

enum Row: Equatable {
    case ask
    case chatGPT
    case hit(Hit)
}

/// Opens chatgpt.com with the question filled in: it runs in the browser on the person's own ChatGPT login.
func openInChatGPT(_ question: String) {
    var allowed = CharacterSet.urlQueryAllowed
    allowed.remove(charactersIn: "&+=?#")
    let q = question.addingPercentEncoding(withAllowedCharacters: allowed) ?? ""
    if let url = URL(string: "https://chatgpt.com/?q=" + q) { NSWorkspace.shared.open(url) }
}

final class LauncherModel: ObservableObject {
    @Published var query = "" {
        didSet {
            guard query != oldValue else { return }
            if answer != nil, !isAnswering {
                // Typing after an answer starts a follow-up, keep it visible until Enter.
                isFollowUp = true
            }
            if answer == nil {
                search.search(query)
                selection = looksLikeQuestion(query) || hits.isEmpty ? 0 : firstHit
            }
        }
    }
    @Published var hits: [Hit] = []
    @Published var selection = 0
    @Published var answer: String?
    @Published var asked = ""
    @Published var isAnswering = false
    @Published var answerError: String?
    @Published var isFollowUp = false

    let search = FileSearch()
    let claude = ClaudeRunner()
    let api = APIRunner()
    let gemini = GeminiRunner()
    let codex = CodexRunner()
    var hide: () -> Void = {}

    init() {
        search.onResults = { [weak self] hits in
            guard let self else { return }
            self.hits = hits
            let maxIndex = hits.count
            if self.selection > maxIndex { self.selection = maxIndex }
            if !self.looksLikeQuestion(self.query), self.selection == 0, !hits.isEmpty { self.selection = self.firstHit }
        }
        // Published fires before the value is stored, so retry on the next turn of the run loop.
        choiceWatch = Settings.shared.$choice.dropFirst().sink { [weak self] _ in
            DispatchQueue.main.async { self?.retryAfterError() }
        }
    }

    private var choiceWatch: AnyCancellable?

    /// Asks the last question again when its answer failed: Enter on an empty bar, or a switch of model.
    func retryAfterError() {
        guard answer != nil, answerError != nil, !isAnswering, !asked.isEmpty else { return }
        claude.reset()
        api.reset()
        gemini.reset()
        codex.reset()
        ask(asked)
    }

    var rows: [Row] {
        guard !query.trimmingCharacters(in: .whitespaces).isEmpty else { return [] }
        return [.ask] + (Settings.shared.showsChatGPT ? [.chatGPT] : []) + hits.map { .hit($0) }
    }

    /// Where the file results start, after the ask rows.
    var firstHit: Int { Settings.shared.showsChatGPT ? 2 : 1 }

    /// ⇧⌘↩ or the ChatGPT row: the typed text, else the question on screen.
    func askChatGPT() {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        let question = text.isEmpty ? asked : text
        guard !question.isEmpty else { return }
        openInChatGPT(question)
        hide()
    }

    func looksLikeQuestion(_ text: String) -> Bool {
        let t = text.trimmingCharacters(in: .whitespaces).lowercased()
        if t.hasSuffix("?") { return true }
        if t.split(separator: " ").count >= 4 { return true }
        let first = String(t.split(separator: " ").first ?? "")
        return Lang.questionWords.contains(first)
    }

    func move(_ delta: Int) {
        let count = rows.count
        guard count > 0 else { return }
        selection = (selection + delta + count) % count
    }

    func activate(forceAsk: Bool = false, reveal: Bool = false) {
        let text = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else {
            retryAfterError()
            return
        }
        if answer != nil || forceAsk || selection == 0 || rows.count <= selection {
            ask(text)
            return
        }
        if rows[selection] == .chatGPT {
            askChatGPT()
            return
        }
        if case let .hit(hit) = rows[selection] {
            if reveal {
                NSWorkspace.shared.activateFileViewerSelecting([hit.url])
            } else {
                NSWorkspace.shared.open(hit.url)
            }
            hide()
        }
    }

    func ask(_ text: String) {
        search.stop()
        asked = text
        answer = ""
        answerError = nil
        isAnswering = true
        isFollowUp = false
        query = ""
        let onText: (String) -> Void = { [weak self] chunk in
            self?.answer = (self?.answer ?? "") + chunk
        }
        let onDone: (String?) -> Void = { [weak self] error in
            self?.isAnswering = false
            self?.answerError = error
        }
        claude.cancel()
        api.cancel()
        gemini.cancel()
        codex.cancel()
        if let connection = Settings.shared.connection {
            api.ask(text, via: connection, onText: onText, onDone: onDone)
        } else if case let .gemini(model) = Settings.shared.choice {
            gemini.ask(text, model: model, onText: onText, onDone: onDone)
        } else if Settings.shared.choice == .codex {
            codex.ask(text, onText: onText, onDone: onDone)
        } else {
            claude.ask(text, onText: onText, onDone: onDone)
        }
    }

    /// Esc: stop an answer, leave the answer view, or hide the panel.
    func escape() {
        if isAnswering {
            claude.cancel()
            api.cancel()
            gemini.cancel()
            codex.cancel()
            isAnswering = false
        } else if answer != nil {
            answer = nil
            answerError = nil
            claude.reset()
            api.reset()
            gemini.reset()
            codex.reset()
            query = ""
        } else if !query.isEmpty {
            query = ""
            hits = []
        } else {
            hide()
        }
    }

    func copyAnswer() {
        guard let a = answer, !a.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(a, forType: .string)
    }

    func resetIfStale() {
        answer = nil
        answerError = nil
        isAnswering = false
        claude.reset()
        api.reset()
        gemini.reset()
        codex.reset()
        query = ""
        hits = []
        selection = 0
    }
}

// MARK: - Views

struct LauncherView: View {
    @ObservedObject var model: LauncherModel
    @ObservedObject var settings = Settings.shared
    @FocusState private var isFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                MascotView(isWalking: model.isAnswering)
                    .frame(width: 30, height: 22)
                TextField(model.answer != nil ? S.followupPlaceholder : S.searchPlaceholder, text: $model.query)
                    .textFieldStyle(.plain)
                    .font(.system(size: 24, weight: .regular))
                    .focused($isFocused)
                    .onSubmit { model.activate() }
                if model.isAnswering {
                    ProgressView().controlSize(.small)
                }
                ModelPicker(isCompact: true)
            }
            .padding(.horizontal, 18)
            .frame(height: 58)

            if model.answer != nil {
                Divider()
                AnswerView(model: model)
            } else if !model.rows.isEmpty {
                Divider()
                ResultsView(model: model)
            }
        }
        .frame(width: 720)
        .id(settings.language)
        .background(VisualEffect().clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous)))
        .overlay(RoundedRectangle(cornerRadius: 26, style: .continuous).strokeBorder(Color.primary.opacity(0.12)))
        .onAppear { isFocused = true }
        .onReceive(NotificationCenter.default.publisher(for: .launcherShown)) { _ in isFocused = true }
    }
}

struct ResultsView: View {
    @ObservedObject var model: LauncherModel

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(spacing: 2) {
                    ForEach(Array(model.rows.enumerated()), id: \.offset) { index, row in
                        RowView(row: row, query: model.query, isSelected: index == model.selection)
                            .id(index)
                            .contentShape(Rectangle())
                            .onTapGesture {
                                model.selection = index
                                model.activate()
                            }
                    }
                }
                .padding(8)
            }
            .frame(maxHeight: 420)
            .fixedSize(horizontal: false, vertical: true)
            .onChange(of: model.selection) { _, s in proxy.scrollTo(s) }
        }
    }
}

struct RowView: View {
    let row: Row
    let query: String
    let isSelected: Bool

    var body: some View {
        HStack(spacing: 10) {
            switch row {
            case .ask:
                MascotView(color: isSelected ? .white : .claude, eyes: isSelected ? nil : .black)
                    .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 1) {
                    Text(S.askRow(Settings.shared.title(of: Settings.shared.choice))).font(.system(size: 14, weight: .semibold))
                    Text(query).font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer()
                Text("⌘↩").font(.system(size: 12)).foregroundStyle(.tertiary)
            case .chatGPT:
                Image(systemName: "bubble.left.and.text.bubble.right.fill")
                    .font(.system(size: 17))
                    .foregroundStyle(isSelected ? Color.white : Color(red: 0.06, green: 0.64, blue: 0.5))
                    .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 1) {
                    Text(S.askChatgpt).font(.system(size: 14, weight: .semibold))
                    Text(S.chatgptNote).font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer()
                Text("⇧⌘↩").font(.system(size: 12)).foregroundStyle(.tertiary)
            case let .hit(hit):
                Image(nsImage: NSWorkspace.shared.icon(forFile: hit.path))
                    .resizable()
                    .frame(width: 28, height: 28)
                VStack(alignment: .leading, spacing: 1) {
                    Text(hit.name).font(.system(size: 14, weight: .medium)).lineLimit(1)
                    Text(hit.isApp ? S.application : hit.subtitle)
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
                Spacer()
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(isSelected ? Color.accentColor.opacity(0.85) : Color.clear)
        )
        .foregroundStyle(isSelected ? Color.white : Color.primary)
    }
}

struct AnswerView: View {
    @ObservedObject var model: LauncherModel

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 10) {
                    Text(model.asked)
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(.secondary)
                    if let answer = model.answer, !answer.isEmpty {
                        Text(markdown(answer))
                            .font(.system(size: 15))
                            .textSelection(.enabled)
                            .lineSpacing(3)
                    } else if model.isAnswering {
                        Text(S.thinking).foregroundStyle(.secondary)
                    }
                    if let error = model.answerError {
                        Text(markdown(error)).foregroundStyle(.red)
                    }
                    Color.clear.frame(height: 1).id("end")
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(18)
            }
            .frame(maxHeight: 460)
            .fixedSize(horizontal: false, vertical: true)
            .onChange(of: model.answer) { _, _ in proxy.scrollTo("end", anchor: .bottom) }
        }
        HStack {
            Text(model.isAnswering ? S.hintStop : S.hintDone("⌘C"))
            Spacer()
        }
        .font(.system(size: 11))
        .foregroundStyle(.tertiary)
        .padding(.horizontal, 18)
        .padding(.bottom, 10)
    }

    private func markdown(_ s: String) -> AttributedString {
        let options = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        return (try? AttributedString(markdown: s, options: options)) ?? AttributedString(s)
    }
}

extension Color {
    static let claude = Color(red: 215 / 255, green: 119 / 255, blue: 87 / 255)
}

/// Clawd, the Claude Code mascot, from the pixel art inside Claude Code itself: C body, E eyes.
/// Proportions follow the terminal welcome screen. `step` 1 is the walking pose: legs shift in, the body hops a pixel.
enum Mascot {
    static let width = 18
    static let height = 11

    private static let rows = [
        "...CCCCCCCCCCCC...",
        "...CCCCCCCCCCCC...",
        "...CCECCCCCCECC...",
        "...CCECCCCCCECC...",
        ".CCCCCCCCCCCCCCCC.",
        ".CCCCCCCCCCCCCCCC.",
        "...CCCCCCCCCCCC...",
        "...CCCCCCCCCCCC...",
    ]
    private static let legs = ["....C.C....C.C....", ".....C.C..C.C....."]

    /// Each lit pixel as (column, row, isEye), row 0 at the top.
    static func pixels(step: Int = 0) -> [(x: Int, y: Int, isEye: Bool)] {
        let hop = step == 1 ? 0 : 1
        var out: [(x: Int, y: Int, isEye: Bool)] = []
        for (r, line) in rows.enumerated() {
            for (c, ch) in line.enumerated() where ch != "." {
                out.append((c, r + hop, ch == "E"))
            }
        }
        // Legs are two pixels tall, stretching to three while the body hops.
        for (c, ch) in legs[step].enumerated() where ch != "." {
            for y in (rows.count + hop)...(rows.count + 2) { out.append((c, y, false)) }
        }
        return out
    }
}

/// Clawd in the panel; it walks while `isWalking`. `eyes` nil cuts the eyes out instead.
struct MascotView: View {
    var color: Color = .claude
    var eyes: Color? = .black
    var isWalking = false

    var body: some View {
        TimelineView(.animation(minimumInterval: 0.18, paused: !isWalking)) { context in
            let step = isWalking ? Int(context.date.timeIntervalSinceReferenceDate / 0.18) % 2 : 0
            Canvas { ctx, size in
                let u = min(size.width / CGFloat(Mascot.width), size.height / CGFloat(Mascot.height))
                let x0 = (size.width - u * CGFloat(Mascot.width)) / 2
                let y0 = (size.height - u * CGFloat(Mascot.height)) / 2
                for p in Mascot.pixels(step: step) {
                    // A hair of overlap so neighbouring pixels never show a seam.
                    let rect = CGRect(x: x0 + CGFloat(p.x) * u, y: y0 + CGFloat(p.y) * u, width: u + 0.4, height: u + 0.4)
                    var layer = ctx
                    if p.isEye, eyes == nil { layer.blendMode = .destinationOut }
                    layer.fill(Path(rect), with: .color(p.isEye ? (eyes ?? color) : color))
                }
            }
            .compositingGroup()
        }
    }
}

struct VisualEffect: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = .popover
        v.blendingMode = .behindWindow
        v.state = .active
        return v
    }

    func updateNSView(_ v: NSVisualEffectView, context: Context) {}
}

extension Notification.Name {
    static let launcherShown = Notification.Name("launcherShown")
}

// MARK: - Panel

final class LauncherPanel: NSPanel {
    var model: LauncherModel?

    override var canBecomeKey: Bool { true }

    override func resignKey() {
        super.resignKey()
        orderOut(nil)
    }

    override func keyDown(with event: NSEvent) {
        if handle(event) { return }
        super.keyDown(with: event)
    }

    /// Keys the panel answers before the text field sees them.
    func handle(_ event: NSEvent) -> Bool {
        guard let model, isKeyWindow else { return false }
        let cmd = event.modifierFlags.contains(.command)
        switch Int(event.keyCode) {
        case kVK_Escape:
            model.escape()
            return true
        case kVK_DownArrow where model.answer == nil:
            model.move(1)
            return true
        case kVK_UpArrow where model.answer == nil:
            model.move(-1)
            return true
        case kVK_Return, kVK_ANSI_KeypadEnter:
            if cmd && event.modifierFlags.contains(.shift) {
                model.askChatGPT()
                return true
            }
            if cmd {
                model.activate(forceAsk: true)
                return true
            }
            if event.modifierFlags.contains(.option) {
                model.activate(reveal: true)
                return true
            }
            return false
        case kVK_ANSI_C where cmd && model.answer != nil && model.query.isEmpty:
            if let editor = firstResponder as? NSTextView, editor.selectedRange().length > 0 { return false }
            model.copyAnswer()
            return true
        default:
            return false
        }
    }
}

// MARK: - Welcome window

/// What the welcome window shows and changes: Claude's sign-in, the login item, the window itself.
final class WelcomeModel: ObservableObject {
    enum Auth { case checking, signedIn, signedOut, missing }

    @Published var auth: Auth = .checking
    @Published var isMascotWalking = false
    @Published var isAddingConnection = false
    let form = ConnectionForm()

    func startAdding() {
        form.reset()
        isAddingConnection = true
    }

    func saveConnection() {
        let c = Connection(
            name: form.name.trimmingCharacters(in: .whitespaces),
            baseURL: form.baseURL.trimmingCharacters(in: .whitespaces),
            model: form.model.trimmingCharacters(in: .whitespaces)
        )
        Settings.shared.add(c, key: form.key.trimmingCharacters(in: .whitespacesAndNewlines))
        isAddingConnection = false
    }
    @Published var launchesAtLogin = SMAppService.mainApp.status == .enabled
    @Published var showsOnLaunch: Bool {
        didSet { UserDefaults.standard.set(showsOnLaunch, forKey: "showsWelcomeOnLaunch") }
    }

    var openSearch: () -> Void = {}
    var onLoginItemChange: () -> Void = {}

    init() {
        showsOnLaunch = UserDefaults.standard.object(forKey: "showsWelcomeOnLaunch") as? Bool ?? true
    }

    /// Asks `claude auth status`, which answers from the local credentials without a model call.
    func checkAuth() {
        guard let binary = ClaudeRunner.binary else {
            auth = .missing
            return
        }
        auth = .checking
        DispatchQueue.global().async {
            let p = Process()
            p.executableURL = URL(fileURLWithPath: binary)
            p.arguments = ["auth", "status"]
            let out = Pipe()
            p.standardOutput = out
            p.standardError = Pipe()
            var isSignedIn = false
            if (try? p.run()) != nil {
                let data = out.fileHandleForReading.readDataToEndOfFile()
                p.waitUntilExit()
                let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
                isSignedIn = json?["loggedIn"] as? Bool ?? false
            }
            DispatchQueue.main.async { self.auth = isSignedIn ? .signedIn : .signedOut }
        }
    }

    /// Opens Terminal on `claude auth login` through a throwaway .command file (no Automation permission needed).
    func signIn() {
        guard let binary = ClaudeRunner.binary else { return }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("claudelight-login.command")
        let script = "#!/bin/zsh\n'\(binary)' auth login\necho\necho '\(S.loginDone.replacingOccurrences(of: "'", with: ""))'\n"
        try? script.write(to: url, atomically: true, encoding: .utf8)
        try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: url.path)
        NSWorkspace.shared.open(url)
    }

    func setLaunchesAtLogin(_ isOn: Bool) {
        do {
            if isOn { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            NSSound.beep()
        }
        launchesAtLogin = SMAppService.mainApp.status == .enabled
        onLoginItemChange()
    }
}

/// The add-connection form's fields.
final class ConnectionForm: ObservableObject {
    @Published var preset: ServicePreset = .openRouter {
        didSet {
            baseURL = preset.baseURL
            if name.isEmpty || ServicePreset.allCases.contains(where: { $0.title == name }) { name = preset.title }
        }
    }
    @Published var name = ServicePreset.openRouter.title
    @Published var baseURL = ServicePreset.openRouter.baseURL
    @Published var model = ""
    @Published var key = ""

    var isValid: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty
            && URL(string: baseURL.trimmingCharacters(in: .whitespaces))?.scheme?.hasPrefix("http") == true
            && !model.trimmingCharacters(in: .whitespaces).isEmpty
    }

    func reset() {
        preset = .openRouter
        name = preset.title
        model = ""
        key = ""
    }
}

struct ConnectionFormView: View {
    @ObservedObject var form: ConnectionForm
    var onCancel: () -> Void
    var onSave: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(S.formTitle).font(.system(size: 17, weight: .semibold))
            Text(S.formSubtitle)
                .font(.system(size: 12)).foregroundStyle(.secondary)

            Form {
                Picker(S.service, selection: $form.preset) {
                    ForEach(ServicePreset.allCases) { Text($0.title).tag($0) }
                }
                TextField(S.name, text: $form.name)
                TextField(S.address, text: $form.baseURL, prompt: Text("https://…/v1"))
                TextField(S.model, text: $form.model, prompt: Text(form.preset.modelHint))
                SecureField(S.apiKey, text: $form.key, prompt: Text(form.preset == .gigaChat ? S.hintGigachatKey : form.preset.needsKey ? S.keyStored : S.keyNotNeeded))
            }
            .formStyle(.columns)
            .font(.system(size: 13))

            HStack {
                Spacer()
                Button(S.cancel, action: onCancel).keyboardShortcut(.cancelAction)
                Button(S.connect, action: onSave)
                    .keyboardShortcut(.defaultAction)
                    .disabled(!form.isValid)
            }
        }
        .padding(22)
        .frame(width: 400)
    }
}

struct WelcomeView: View {
    @ObservedObject var model: WelcomeModel
    @ObservedObject var settings = Settings.shared

    var body: some View {
        VStack(spacing: 0) {
            MascotView(isWalking: model.isMascotWalking)
                .frame(width: 108, height: 66)
                .onHover { model.isMascotWalking = $0 }
                .padding(.top, 34)

            Text("ClaudeLight")
                .font(.system(size: 26, weight: .bold, design: .rounded))
                .padding(.top, 14)
            Text(S.welcomeSubtitle)
                .font(.system(size: 13))
                .foregroundStyle(.secondary)
                .padding(.top, 4)

            HStack(spacing: 6) {
                KeyCap("⌥")
                KeyCap(hotkeyKey)
                Text(S.hotkeyHint)
                    .font(.system(size: 13))
                    .foregroundStyle(.secondary)
            }
            .padding(.top, 22)

            VStack(spacing: 0) {
                authRow
                Divider().padding(.leading, 14)
                HStack {
                    Text(S.model)
                    Spacer()
                    ModelPicker()
                }
                .padding(.horizontal, 14).padding(.vertical, 10)
                Divider().padding(.leading, 14)
                HStack {
                    Text(S.language)
                    Spacer()
                    LanguagePicker()
                }
                .padding(.horizontal, 14).padding(.vertical, 6)
                ForEach(settings.connections) { c in
                    Divider().padding(.leading, 14)
                    HStack(spacing: 8) {
                        Image(systemName: "link").foregroundStyle(.secondary)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(c.name)
                            Text(c.model).font(.system(size: 11)).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button {
                            settings.remove(c)
                        } label: {
                            Image(systemName: "trash")
                        }
                        .buttonStyle(.borderless)
                        .help(S.deleteConnection)
                    }
                    .padding(.horizontal, 14).padding(.vertical, 8)
                }
                Divider().padding(.leading, 14)
                HStack {
                    Button(S.connectModel, action: model.startAdding)
                        .buttonStyle(.borderless)
                        .foregroundStyle(Color.claude)
                    Spacer()
                }
                .padding(.horizontal, 14).padding(.vertical, 10)
                Divider().padding(.leading, 14)
                Toggle(S.launchAtLogin, isOn: Binding(
                    get: { model.launchesAtLogin },
                    set: { model.setLaunchesAtLogin($0) }
                ))
                .padding(.horizontal, 14).padding(.vertical, 10)
                Divider().padding(.leading, 14)
                Toggle(S.showOnLaunch, isOn: $model.showsOnLaunch)
                    .padding(.horizontal, 14).padding(.vertical, 10)
                Divider().padding(.leading, 14)
                Toggle(S.showChatgpt, isOn: $settings.showsChatGPT)
                    .padding(.horizontal, 14).padding(.vertical, 10)
            }
            .toggleStyle(.switch)
            .controlSize(.small)
            .font(.system(size: 13))
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
            .padding(.horizontal, 24)
            .padding(.top, 22)

            Spacer(minLength: 22)

            Button(action: model.openSearch) {
                Text(S.openSearch)
                    .font(.system(size: 14, weight: .semibold))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 6)
            }
            .buttonStyle(.borderedProminent)
            .tint(.claude)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)
            .padding(.horizontal, 24)
            .padding(.bottom, 24)
        }
        .frame(width: 400)
        .id(settings.language)
        .frame(minHeight: 560)
        .fixedSize(horizontal: false, vertical: true)
        .background(VisualEffect())
        .sheet(isPresented: $model.isAddingConnection) {
            ConnectionFormView(form: model.form, onCancel: { model.isAddingConnection = false }, onSave: model.saveConnection)
        }
        .onAppear { model.checkAuth() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            model.checkAuth()
        }
    }

    @ViewBuilder private var authRow: some View {
        HStack(spacing: 8) {
            switch model.auth {
            case .checking:
                ProgressView().controlSize(.mini)
                Text(S.authChecking).foregroundStyle(.secondary)
                Spacer()
            case .signedIn:
                Circle().fill(Color.green).frame(width: 8, height: 8)
                Text(S.authOk)
                Spacer()
            case .signedOut:
                Circle().fill(Color.orange).frame(width: 8, height: 8)
                Text(S.authSignedOut)
                Spacer()
                Button(S.signIn, action: model.signIn).controlSize(.small)
            case .missing:
                Circle().fill(Color.red).frame(width: 8, height: 8)
                Text(S.authMissing)
                Spacer()
                Link(S.download, destination: URL(string: "https://claude.com/claude-code")!)
            }
        }
        .font(.system(size: 13))
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}

struct KeyCap: View {
    let label: String
    init(_ label: String) { self.label = label }

    var body: some View {
        Text(label)
            .font(.system(size: 13, weight: .medium, design: .rounded))
            .frame(minWidth: 26, minHeight: 24)
            .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.primary.opacity(0.08)))
            .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Color.primary.opacity(0.15)))
    }
}

// MARK: - App

/// Appends a line to ~/Library/Logs/ClaudeLight.log.
func debugLog(_ line: String) {
    let url = URL(fileURLWithPath: NSHomeDirectory() + "/Library/Logs/ClaudeLight.log")
    let data = Data("\(Date()) \(line)\n".utf8)
    if let h = try? FileHandle(forWritingTo: url) {
        h.seekToEndOfFile()
        h.write(data)
        try? h.close()
    } else {
        try? data.write(to: url)
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var panel: LauncherPanel!
    private let model = LauncherModel()
    private var statusItem: NSStatusItem!
    private var hotKey: EventHotKeyRef?
    private var isoHotKey: EventHotKeyRef?
    private var monitor: Any?
    private var hiddenAt = Date.distantPast
    private let welcome = WelcomeModel()
    private var welcomeWindow: NSWindow?
    private var loginItem: NSMenuItem?
    private var languageWatch: AnyCancellable?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        buildPanel()
        buildMenu()
        registerHotKey()

        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let panel = self?.panel, event.window === panel else { return event }
            return panel.handle(event) ? nil : event
        }
        model.hide = { [weak self] in self?.hidePanel() }
        welcome.openSearch = { [weak self] in
            self?.welcomeWindow?.close()
            self?.showPanel()
        }
        welcome.onLoginItemChange = { [weak self] in self?.syncLoginItem() }
        NotificationCenter.default.addObserver(self, selector: #selector(addConnection), name: .addConnection, object: nil)

        if welcome.showsOnLaunch && !Self.launchedAsLoginItem() { showWelcome() }
    }

    /// Opening the app again while it runs (Finder, Launchpad, Spotlight) brings the welcome window back.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showWelcome()
        return false
    }

    private static func launchedAsLoginItem() -> Bool {
        guard let event = NSAppleEventManager.shared().currentAppleEvent else { return false }
        return event.eventID == kAEOpenApplication
            && event.paramDescriptor(forKeyword: keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
    }

    @objc func showWelcome() {
        if welcomeWindow == nil {
            let window = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 400, height: 520),
                styleMask: [.titled, .closable, .fullSizeContentView],
                backing: .buffered,
                defer: false
            )
            window.titlebarAppearsTransparent = true
            window.titleVisibility = .hidden
            window.isMovableByWindowBackground = true
            window.isReleasedWhenClosed = false
            let host = NSHostingController(rootView: WelcomeView(model: welcome))
            host.sizingOptions = [.preferredContentSize]
            window.contentViewController = host
            window.center()
            welcomeWindow = window
        }
        NSApp.activate(ignoringOtherApps: true)
        welcomeWindow?.makeKeyAndOrderFront(nil)
    }

    /// Rebuilds the model submenu each time it opens: Claude models, then connections, ticking the current one.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let settings = Settings.shared
        func add(_ title: String, _ choice: Choice) {
            let item = NSMenuItem(title: title, action: #selector(pickModel(_:)), keyEquivalent: "")
            item.representedObject = choice.stored
            item.target = self
            item.state = settings.choice == choice ? .on : .off
            menu.addItem(item)
        }
        for m in ClaudeModel.allCases { add("\(m.title) — \(m.note)", .claude(m)) }
        menu.addItem(.separator())
        menu.addItem(.separator())
        add("\(S.codexTitle) — \(S.noteCodex)", .codex)
        menu.addItem(.separator())
        for m in GeminiModel.allCases { add("\(m.title) — \(m.note)", .gemini(m)) }
        if !settings.connections.isEmpty {
            menu.addItem(.separator())
            for c in settings.connections { add("\(c.name) — \(c.model)", .custom(c.id)) }
        }
        menu.addItem(.separator())
        let item = NSMenuItem(title: S.connectModel, action: #selector(addConnection), keyEquivalent: "")
        item.target = self
        menu.addItem(item)
    }

    private func syncLoginItem() {
        loginItem?.state = SMAppService.mainApp.status == .enabled ? .on : .off
        welcome.launchesAtLogin = SMAppService.mainApp.status == .enabled
    }

    private func buildPanel() {
        panel = LauncherPanel(
            contentRect: NSRect(x: 0, y: 0, width: 720, height: 58),
            styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        panel.model = model
        panel.isFloatingPanel = true
        panel.level = .modalPanel
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.isMovableByWindowBackground = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]

        // The controller resizes the window to the SwiftUI content; resized() then pins the top edge.
        let host = NSHostingController(rootView: LauncherView(model: model))
        host.sizingOptions = [.preferredContentSize]
        panel.contentViewController = host
        host.view.wantsLayer = true
        host.view.layer?.backgroundColor = .clear
        host.view.layer?.cornerRadius = 26
        host.view.layer?.cornerCurve = .continuous
        host.view.layer?.masksToBounds = true
        panel.setContentSize(NSSize(width: 720, height: 58))
        NotificationCenter.default.addObserver(self, selector: #selector(resized), name: NSWindow.didResizeNotification, object: panel)
        NotificationCenter.default.addObserver(self, selector: #selector(panelHidden), name: NSWindow.didResignKeyNotification, object: panel)
    }

    /// Keeps the top edge fixed as results and answers grow the panel downward.
    @objc private func resized() {
        guard let screen = panel.screen ?? NSScreen.main else { return }
        var size = panel.frame.size
        if size.width < 100 || size.height < 58 { size = NSSize(width: 720, height: 58) }
        let top = screen.visibleFrame.maxY - screen.visibleFrame.height * 0.22
        let x = screen.visibleFrame.midX - size.width / 2
        let frame = NSRect(x: x, y: top - size.height, width: size.width, height: size.height)
        if panel.frame != frame {
            panel.setFrame(frame, display: true)
            panel.invalidateShadow()
        }
    }

    @objc private func panelHidden() { hiddenAt = Date() }

    private func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = Self.sparkImage()
        rebuildMenu()
        // Published fires before the value is stored, so rebuild on the next turn of the run loop.
        languageWatch = Settings.shared.$language.dropFirst().sink { [weak self] _ in
            DispatchQueue.main.async { self?.rebuildMenu() }
        }
    }

    private func rebuildMenu() {
        let menu = NSMenu()
        menu.addItem(withTitle: S.menuOpenSearch("⌥ " + hotkeyKey), action: #selector(togglePanel), keyEquivalent: "")
        menu.addItem(withTitle: S.menuWindow, action: #selector(showWelcome), keyEquivalent: "")
        let login = NSMenuItem(title: S.menuLaunchAtLogin, action: #selector(toggleLogin(_:)), keyEquivalent: "")
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        loginItem = login
        let models = NSMenu()
        models.delegate = self
        let modelItem = NSMenuItem(title: S.model, action: nil, keyEquivalent: "")
        modelItem.submenu = models
        menu.addItem(modelItem)
        menu.addItem(.separator())
        menu.addItem(withTitle: S.menuQuit, action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        for item in menu.items where item.action != #selector(NSApplication.terminate(_:)) { item.target = self }
        statusItem.menu = menu
    }

    /// The menu bar Clawd, a template image (eyes cut out) so it follows the menu bar's colour.
    static func sparkImage() -> NSImage {
        let image = NSImage(size: NSSize(width: 18, height: 14), flipped: true) { _ in
            NSColor.black.setFill()
            for p in Mascot.pixels() where !p.isEye {
                NSRect(x: CGFloat(p.x), y: 1 + CGFloat(p.y - 1), width: 1, height: 1).fill()
            }
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "ClaudeLight"
        return image
    }

    @objc private func pickModel(_ item: NSMenuItem) {
        Settings.shared.choice = Choice(stored: item.representedObject as? String ?? "")
    }

    @objc private func addConnection() {
        showWelcome()
        welcome.startAdding()
    }

    @objc private func toggleLogin(_ item: NSMenuItem) {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            NSSound.beep()
        }
        syncLoginItem()
    }

    private func registerHotKey() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, data in
            guard let data else { return noErr }
            let delegate = Unmanaged<AppDelegate>.fromOpaque(data).takeUnretainedValue()
            DispatchQueue.main.async { delegate.togglePanel() }
            return noErr
        }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), nil)

        let id = EventHotKeyID(signature: OSType(0x434C4C54), id: 1) // 'CLLT'
        // Option + the key left of 1: ANSI keyboards report it as Grave, ISO (European) ones as Section.
        let status = RegisterEventHotKey(UInt32(kVK_ANSI_Grave), UInt32(optionKey), id, GetApplicationEventTarget(), 0, &hotKey)
        let isoStatus = RegisterEventHotKey(UInt32(kVK_ISO_Section), UInt32(optionKey), id, GetApplicationEventTarget(), 0, &isoHotKey)
        debugLog("hotkey registered, status \(status), iso \(isoStatus)")
    }

    @objc func togglePanel() {
        debugLog("toggle, visible=\(panel.isVisible)")
        panel.isVisible ? hidePanel() : showPanel()
    }

    private func showPanel() {
        // A fresh start after a few minutes away, like Spotlight; a quick reopen keeps the answer.
        if Date().timeIntervalSince(hiddenAt) > 300 { model.resetIfStale() }
        resized()
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        NotificationCenter.default.post(name: .launcherShown, object: nil)
    }

    private func hidePanel() {
        panel.orderOut(nil)
        hiddenAt = Date()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
