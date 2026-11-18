package net.rxnel.companion;

import android.annotation.SuppressLint;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.webkit.JavascriptInterface;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;

/**
 * Rx | NeL 手机端：一个 WebView 壳。
 * 首次打开让你填电脑地址（http://192.168.x.x:27150），之后直接进遥控页面。
 */
public class MainActivity extends AppCompatActivity {

    private WebView web;
    private SharedPreferences prefs;

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        prefs = getSharedPreferences("rxnel", MODE_PRIVATE);

        web = new WebView(this);
        setContentView(web);

        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(true);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);

        web.setWebViewClient(new WebViewClient());
        web.addJavascriptInterface(new Bridge(), "Android");

        String host = prefs.getString("host", "");

        if (host.isEmpty()) {
            web.loadUrl("file:///android_asset/setup.html");
        } else {
            web.loadUrl(host);
        }
    }

    public class Bridge {

        @JavascriptInterface
        public void saveAndOpen(String host) {
            String normalized = host.trim();

            if (!normalized.startsWith("http")) {
                normalized = "http://" + normalized;
            }

            if (!normalized.contains(":27150")) {
                normalized = normalized.replaceAll("/+$", "") + ":27150/";
            }

            prefs.edit().putString("host", normalized).apply();

            final String target = normalized;

            runOnUiThread(() -> {
                Toast.makeText(MainActivity.this, "已保存：" + target, Toast.LENGTH_SHORT).show();
                web.loadUrl(target);
            });
        }

        @JavascriptInterface
        public void reset() {
            prefs.edit().remove("host").apply();
            runOnUiThread(() -> web.loadUrl("file:///android_asset/setup.html"));
        }
    }

    @Override
    public void onBackPressed() {
        if (web.canGoBack()) {
            web.goBack();
        } else {
            super.onBackPressed();
        }
    }
}
