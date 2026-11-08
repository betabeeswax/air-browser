package com.air.browser

import android.app.Activity
import android.app.AlertDialog
import android.app.DownloadManager
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Bundle
import android.os.Environment
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import android.webkit.CookieManager
import android.webkit.DownloadListener
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupMenu
import android.widget.TextView
import android.widget.Toast
import org.json.JSONArray
import org.json.JSONObject
import java.net.URLEncoder

class MainActivity : Activity() {
    private data class BrowserTab(
        val id: Int,
        val webView: WebView,
        var title: String = "New tab",
        var url: String = HOME_BASE
    )

    companion object {
        private const val HOME_BASE = "https://air-browser.local/"
        private const val PREFS = "air_browser_local"
        private const val MAX_RESTORED_TABS = 8
    }

    private val prefs by lazy { getSharedPreferences(PREFS, Context.MODE_PRIVATE) }
    private val tabs = mutableListOf<BrowserTab>()
    private var nextTabId = 1
    private var activeTabId = -1

    private lateinit var root: LinearLayout
    private lateinit var webContainer: FrameLayout
    private lateinit var addressBar: EditText
    private lateinit var tabCount: TextView
    private lateinit var backButton: TextView
    private lateinit var forwardButton: TextView
    private lateinit var reloadButton: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.statusBarColor = Color.rgb(11, 16, 32)
        window.navigationBarColor = Color.rgb(11, 16, 32)
        window.decorView.systemUiVisibility = 0
        CookieManager.getInstance().setAcceptCookie(true)
        buildInterface()
        restoreTabs()
    }

    private fun buildInterface() {
        root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.rgb(11, 16, 32))
            setPadding(dp(10), dp(6), dp(10), dp(4))
        }

        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(5), 0, 0, 0)
        }
        val brand = TextView(this).apply {
            text = "AIR"
            textSize = 22f
            setTextColor(Color.rgb(76, 169, 255))
            typeface = Typeface.DEFAULT_BOLD
        }
        val browserLabel = TextView(this).apply {
            text = "  Browser"
            textSize = 18f
            setTextColor(Color.WHITE)
        }
        header.addView(brand)
        header.addView(browserLabel)
        header.addView(View(this), LinearLayout.LayoutParams(0, dp(1), 1f))
        tabCount = actionText("▢ 1") { showTabsDialog() }
        header.addView(tabCount, buttonParams())
        header.addView(actionText("⋮") { showMainMenu(it) }, buttonParams())
        root.addView(header, LinearLayout.LayoutParams(-1, dp(46)))

        val addressRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        backButton = actionText("‹") { goBack() }
        forwardButton = actionText("›") { goForward() }
        reloadButton = actionText("↻") { reloadPage() }
        addressRow.addView(backButton, buttonParams())
        addressRow.addView(forwardButton, buttonParams())
        addressRow.addView(reloadButton, buttonParams())

        addressBar = EditText(this).apply {
            hint = "Search or enter website"
            setHintTextColor(Color.rgb(160, 173, 195))
            setTextColor(Color.WHITE)
            textSize = 15f
            singleLine = true
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_URI
            imeOptions = EditorInfo.IME_ACTION_GO
            setPadding(dp(13), 0, dp(8), 0)
            background = rounded(Color.rgb(28, 39, 61), dp(16))
            setSelectAllOnFocus(true)
            setOnEditorActionListener { _, actionId, _ ->
                if (actionId == EditorInfo.IME_ACTION_GO ||
                    actionId == EditorInfo.IME_ACTION_SEARCH ||
                    actionId == EditorInfo.IME_ACTION_DONE
                ) {
                    navigateFromAddress()
                    true
                } else false
            }
        }
        addressRow.addView(addressBar, LinearLayout.LayoutParams(0, dp(42), 1f).apply {
            setMargins(dp(3), 0, dp(3), 0)
        })
        addressRow.addView(actionText("Go") { navigateFromAddress() }, buttonParams())
        root.addView(addressRow, LinearLayout.LayoutParams(-1, dp(52)))

        webContainer = FrameLayout(this).apply {
            setBackgroundColor(Color.WHITE)
        }
        root.addView(webContainer, LinearLayout.LayoutParams(-1, 0, 1f))

        val bottom = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            setPadding(0, dp(3), 0, 0)
        }
        bottom.addView(bottomAction("⌂", "Home") { loadHome() }, equalWeight())
        bottom.addView(bottomAction("☆", "Bookmarks") { showBookmarks() }, equalWeight())
        bottom.addView(bottomAction("↓", "Downloads") { openDownloads() }, equalWeight())
        root.addView(bottom, LinearLayout.LayoutParams(-1, dp(48)))

        setContentView(root)
        updateNavigationButtons()
    }

    private fun restoreTabs() {
        val saved = try {
            JSONArray(prefs.getString("open_tabs", "[]") ?: "[]")
        } catch (_: Exception) {
            JSONArray()
        }
        if (saved.length() == 0) {
            addTab(HOME_BASE)
            return
        }
        val count = minOf(saved.length(), MAX_RESTORED_TABS)
        for (i in 0 until count) {
            val url = saved.optString(i, HOME_BASE).ifBlank { HOME_BASE }
            addTab(url, switchTo = i == count - 1)
        }
    }

    private fun addTab(initialUrl: String = HOME_BASE, switchTo: Boolean = true): BrowserTab {
        val webView = WebView(this)
        webView.setBackgroundColor(Color.WHITE)
        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true
        webView.settings.loadsImagesAutomatically = true
        webView.settings.javaScriptCanOpenWindowsAutomatically = true
        webView.settings.setSupportMultipleWindows(true)
        webView.settings.builtInZoomControls = true
        webView.settings.displayZoomControls = false

        val tab = BrowserTab(nextTabId++, webView)
        tabs.add(tab)
        webContainer.addView(webView, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ))

        webView.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val uri = request.url
                val scheme = uri.scheme?.lowercase()
                if (scheme == "http" || scheme == "https") return false
                return try {
                    startActivity(Intent(Intent.ACTION_VIEW, uri))
                    true
                } catch (_: Exception) {
                    Toast.makeText(this@MainActivity, "This link can't be opened.", Toast.LENGTH_SHORT).show()
                    true
                }
            }

            override fun onPageStarted(view: WebView, url: String, favicon: android.graphics.Bitmap?) {
                tab.url = url
                if (activeTabId == tab.id) {
                    addressBar.setText(if (isHome(url)) "" else url)
                    updateNavigationButtons()
                }
            }

            override fun onPageFinished(view: WebView, url: String) {
                tab.url = url
                tab.title = view.title?.takeIf { it.isNotBlank() } ?: if (isHome(url)) "New tab" else url
                if (activeTabId == tab.id) {
                    addressBar.setText(if (isHome(url)) "" else url)
                    updateNavigationButtons()
                }
                if (!isHome(url)) addHistory(tab.title, url)
                saveTabs()
            }
        }

        webView.webChromeClient = object : WebChromeClient() {
            override fun onReceivedTitle(view: WebView, title: String?) {
                if (!title.isNullOrBlank()) {
                    tab.title = title
                    updateTabCount()
                }
            }

            override fun onCreateWindow(
                view: WebView,
                isDialog: Boolean,
                isUserGesture: Boolean,
                resultMsg: android.os.Message
            ): Boolean {
                val newTab = addTab(HOME_BASE, true)
                val transport = resultMsg.obj as? WebView.WebViewTransport ?: return false
                transport.webView = newTab.webView
                resultMsg.sendToTarget()
                return true
            }
        }

        webView.setDownloadListener(DownloadListener { url, userAgent, contentDisposition, mimeType, _ ->
            startDownload(url, userAgent, contentDisposition, mimeType)
        })

        if (switchTo) switchToTab(tab.id)
        if (isHome(initialUrl)) showHomeIn(tab) else webView.loadUrl(initialUrl)
        saveTabs()
        return tab
    }

    private fun switchToTab(id: Int) {
        val tab = tabs.firstOrNull { it.id == id } ?: return
        activeTabId = id
        tabs.forEach { it.webView.visibility = if (it.id == id) View.VISIBLE else View.GONE }
        addressBar.setText(if (isHome(tab.url)) "" else tab.url)
        updateTabCount()
        updateNavigationButtons()
    }

    private fun closeTab(id: Int) {
        val index = tabs.indexOfFirst { it.id == id }
        if (index < 0) return
        val wasActive = activeTabId == id
        val tab = tabs.removeAt(index)
        webContainer.removeView(tab.webView)
        tab.webView.stopLoading()
        tab.webView.destroy()
        if (tabs.isEmpty()) {
            addTab(HOME_BASE)
        } else if (wasActive) {
            switchToTab(tabs[minOf(index, tabs.lastIndex)].id)
        }
        saveTabs()
        updateTabCount()
    }

    private fun showTabsDialog() {
        if (tabs.isEmpty()) return
        val labels = tabs.mapIndexed { index, tab ->
            "${index + 1}. ${tab.title.ifBlank { "New tab" }}"
        }.toTypedArray()
        val dialog = AlertDialog.Builder(this)
            .setTitle("Open tabs")
            .setItems(labels) { _, which -> switchToTab(tabs[which].id) }
            .setPositiveButton("New tab") { _, _ -> addTab() }
            .setNegativeButton("Close", null)
            .create()
        dialog.setOnShowListener {
            dialog.listView.setOnItemLongClickListener { _, _, position, _ ->
                val tab = tabs.getOrNull(position)
                if (tab != null && tabs.size > 1) closeTab(tab.id)
                dialog.dismiss()
                true
            }
        }
        dialog.show()
    }

    private fun navigateFromAddress() {
        val raw = addressBar.text.toString().trim()
        if (raw.isEmpty()) return
        val encoded = try { URLEncoder.encode(raw, "UTF-8") } catch (_: Exception) { raw }
        val target = when {
            raw.startsWith("https://", true) || raw.startsWith("http://", true) -> raw
            raw.contains(" ") || !raw.contains(".") -> "https://www.google.com/search?q=$encoded"
            else -> "https://$raw"
        }
        val tab = activeTab() ?: addTab()
        tab.url = target
        tab.webView.loadUrl(target)
        addressBar.clearFocus()
    }

    private fun loadHome() {
        val tab = activeTab() ?: addTab()
        showHomeIn(tab)
    }

    private fun showHomeIn(tab: BrowserTab) {
        tab.url = HOME_BASE
        tab.title = "New tab"
        tab.webView.loadDataWithBaseURL(HOME_BASE, homeHtml(), "text/html", "UTF-8", null)
        if (activeTabId == tab.id) {
            addressBar.setText("")
            updateNavigationButtons()
        }
        saveTabs()
    }

    private fun homeHtml(): String = """
        <!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1">
        <style>
        *{box-sizing:border-box}body{margin:0;background:#0b1020;color:#f5f8ff;font-family:Arial,sans-serif;min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px}
        main{width:100%;max-width:520px;text-align:center;margin-top:-7vh}.logo{font-size:54px;font-weight:800;letter-spacing:3px;color:#55adff}.sub{color:#9eacc5;margin:6px 0 30px}
        form{display:flex;gap:8px;background:#1c273d;border:1px solid #314361;border-radius:18px;padding:7px}
        input{min-width:0;flex:1;background:transparent;border:0;outline:0;color:white;font-size:16px;padding:10px}
        button{border:0;border-radius:13px;background:#2789e8;color:white;font-weight:700;padding:0 17px;font-size:15px}
        .links{display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin-top:28px}
        a{color:#dce9ff;text-decoration:none;background:#151f33;border:1px solid #263650;border-radius:15px;padding:16px 6px;font-size:14px}
        small{display:block;color:#8292ad;margin-top:32px}
        </style></head><body><main><div class="logo">AIR</div><div class="sub">A clearer way to browse.</div>
        <form onsubmit="event.preventDefault();var q=document.getElementById('q').value.trim();if(q)location.href='https://www.google.com/search?q='+encodeURIComponent(q)">
        <input id="q" placeholder="Search the web or enter a website" autocomplete="off"><button type="submit">Search</button></form>
        <div class="links"><a href="https://www.google.com">Google</a><a href="https://www.youtube.com">YouTube</a><a href="https://www.wikipedia.org">Wikipedia</a></div>
        <small>Your tabs and bookmarks stay on this device.</small></main></body></html>
    """.trimIndent()

    private fun goBack() {
        val web = activeTab()?.webView ?: return
        if (web.canGoBack()) web.goBack()
    }

    private fun goForward() {
        val web = activeTab()?.webView ?: return
        if (web.canGoForward()) web.goForward()
    }

    private fun reloadPage() {
        val web = activeTab()?.webView ?: return
        if (isHome(activeTab()?.url)) loadHome() else web.reload()
    }

    private fun updateNavigationButtons() {
        val web = activeTab()?.webView
        backButton.alpha = if (web?.canGoBack() == true) 1f else 0.42f
        forwardButton.alpha = if (web?.canGoForward() == true) 1f else 0.42f
        reloadButton.alpha = 1f
    }

    private fun updateTabCount() {
        tabCount.text = "▢ ${tabs.size}"
    }

    private fun showMainMenu(anchor: View) {
        val popup = PopupMenu(this, anchor)
        popup.menu.add(0, 1, 0, "New tab")
        popup.menu.add(0, 2, 1, "Add bookmark")
        popup.menu.add(0, 3, 2, "Bookmarks")
        popup.menu.add(0, 4, 3, "History")
        popup.menu.add(0, 5, 4, "Downloads")
        popup.menu.add(0, 6, 5, "Share page")
        popup.menu.add(0, 7, 6, "Clear browsing history")
        popup.menu.add(0, 8, 7, "About AIR Browser")
        popup.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                1 -> addTab()
                2 -> addBookmark()
                3 -> showBookmarks()
                4 -> showHistory()
                5 -> openDownloads()
                6 -> sharePage()
                7 -> clearHistory()
                8 -> AlertDialog.Builder(this).setTitle("AIR Browser")
                    .setMessage("AIR Browser for Android\nEarly testing version")
                    .setPositiveButton("OK", null).show()
            }
            true
        }
        popup.show()
    }

    private fun addBookmark() {
        val tab = activeTab() ?: return
        if (isHome(tab.url)) {
            Toast.makeText(this, "Open a website first.", Toast.LENGTH_SHORT).show()
            return
        }
        val items = readArray("bookmarks")
        val duplicate = (0 until items.length()).any { items.optJSONObject(it)?.optString("url") == tab.url }
        if (duplicate) {
            Toast.makeText(this, "Already bookmarked.", Toast.LENGTH_SHORT).show()
            return
        }
        items.put(JSONObject().put("title", tab.title).put("url", tab.url))
        prefs.edit().putString("bookmarks", items.toString()).apply()
        Toast.makeText(this, "Bookmark saved.", Toast.LENGTH_SHORT).show()
    }

    private fun showBookmarks() = showSavedList("Bookmarks", "bookmarks", false)
    private fun showHistory() = showSavedList("History", "history", true)

    private fun showSavedList(title: String, key: String, history: Boolean) {
        val items = readArray(key)
        val entries = mutableListOf<JSONObject>()
        for (i in items.length() - 1 downTo 0) {
            items.optJSONObject(i)?.let { entries.add(it) }
        }
        if (entries.isEmpty()) {
            AlertDialog.Builder(this).setTitle(title).setMessage("Nothing here yet.")
                .setPositiveButton("OK", null).show()
            return
        }
        val labels = entries.map { it.optString("title", it.optString("url")) }.toTypedArray()
        AlertDialog.Builder(this).setTitle(title).setItems(labels) { _, which ->
            val url = entries[which].optString("url")
            if (url.isNotBlank()) {
                val tab = activeTab() ?: addTab()
                tab.webView.loadUrl(url)
            }
        }.setNegativeButton("Close", null).show()
    }

    private fun addHistory(title: String, url: String) {
        if (url.isBlank() || isHome(url)) return
        val items = readArray("history")
        if (items.length() > 0 && items.optJSONObject(items.length() - 1)?.optString("url") == url) return
        items.put(JSONObject().put("title", title).put("url", url).put("visited", System.currentTimeMillis()))
        while (items.length() > 100) items.remove(0)
        prefs.edit().putString("history", items.toString()).apply()
    }

    private fun clearHistory() {
        AlertDialog.Builder(this).setTitle("Clear browsing history?")
            .setMessage("This removes the history saved by AIR Browser on this device.")
            .setNegativeButton("Cancel", null)
            .setPositiveButton("Clear") { _, _ ->
                prefs.edit().remove("history").apply()
                Toast.makeText(this, "History cleared.", Toast.LENGTH_SHORT).show()
            }.show()
    }

    private fun openDownloads() {
        try {
            startActivity(Intent(DownloadManager.ACTION_VIEW_DOWNLOADS))
        } catch (_: Exception) {
            Toast.makeText(this, "Downloads are managed by Android.", Toast.LENGTH_SHORT).show()
        }
    }

    private fun startDownload(url: String, userAgent: String?, contentDisposition: String?, mimeType: String?) {
        try {
            val fileName = android.webkit.URLUtil.guessFileName(url, contentDisposition, mimeType)
                .replace(Regex("[^A-Za-z0-9._ -]"), "_")
            val request = DownloadManager.Request(Uri.parse(url))
                .setTitle(fileName)
                .setDescription("Downloading with AIR Browser")
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                .setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, fileName)
            if (!userAgent.isNullOrBlank()) request.addRequestHeader("User-Agent", userAgent)
            val manager = getSystemService(DOWNLOAD_SERVICE) as DownloadManager
            manager.enqueue(request)
            Toast.makeText(this, "Download started.", Toast.LENGTH_SHORT).show()
        } catch (_: Exception) {
            Toast.makeText(this, "Couldn't start this download.", Toast.LENGTH_LONG).show()
        }
    }

    private fun sharePage() {
        val tab = activeTab() ?: return
        if (isHome(tab.url)) return
        val send = Intent(Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(Intent.EXTRA_TEXT, tab.url)
        }
        startActivity(Intent.createChooser(send, "Share page"))
    }

    private fun readArray(key: String): JSONArray = try {
        JSONArray(prefs.getString(key, "[]") ?: "[]")
    } catch (_: Exception) {
        JSONArray()
    }

    private fun saveTabs() {
        if (!::webContainer.isInitialized) return
        val array = JSONArray()
        tabs.forEach { array.put(if (isHome(it.url)) HOME_BASE else it.url) }
        prefs.edit().putString("open_tabs", array.toString()).apply()
    }

    private fun activeTab(): BrowserTab? = tabs.firstOrNull { it.id == activeTabId }

    private fun isHome(url: String?): Boolean =
        url.isNullOrBlank() || url == HOME_BASE || url.startsWith("https://air-browser.local/")

    private fun actionText(label: String, action: (View) -> Unit): TextView =
        TextView(this).apply {
            text = label
            textSize = if (label == "⋮") 25f else 21f
            gravity = Gravity.CENTER
            setTextColor(Color.WHITE)
            setPadding(dp(9), 0, dp(9), 0)
            isClickable = true
            isFocusable = true
            setOnClickListener(action)
        }

    private fun bottomAction(symbol: String, label: String, action: () -> Unit): LinearLayout =
        LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            isClickable = true
            isFocusable = true
            addView(TextView(this@MainActivity).apply {
                text = symbol
                textSize = 20f
                gravity = Gravity.CENTER
                setTextColor(Color.rgb(93, 177, 255))
            })
            addView(TextView(this@MainActivity).apply {
                text = label
                textSize = 11f
                gravity = Gravity.CENTER
                setTextColor(Color.rgb(194, 207, 229))
            })
            setOnClickListener { action() }
        }

    private fun buttonParams() = LinearLayout.LayoutParams(dp(42), dp(42))
    private fun equalWeight() = LinearLayout.LayoutParams(0, -1, 1f)

    private fun rounded(color: Int, radius: Int) = GradientDrawable().apply {
        setColor(color)
        cornerRadius = radius.toFloat()
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    override fun onPause() {
        tabs.forEach { it.webView.onPause() }
        saveTabs()
        super.onPause()
    }

    override fun onResume() {
        super.onResume()
        tabs.forEach { it.webView.onResume() }
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        val web = activeTab()?.webView
        if (web?.canGoBack() == true) {
            web.goBack()
        } else if (tabs.size > 1) {
            closeTab(activeTabId)
        } else {
            super.onBackPressed()
        }
    }

    override fun onDestroy() {
        tabs.forEach {
            it.webView.stopLoading()
            it.webView.destroy()
        }
        tabs.clear()
        super.onDestroy()
    }
}
