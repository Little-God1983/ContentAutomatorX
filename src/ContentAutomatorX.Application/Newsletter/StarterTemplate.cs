namespace ContentAutomatorX.Application.Newsletter;

/// <summary>The HTML seeded into a brand-new newsletter template (Recipes.razor's NewTemplateAsync).
/// Lives here rather than in the Web project so TemplateHtmlRendererTests can render it directly
/// against section data without a Blazor dependency, and so there is exactly one copy to keep in
/// sync with TemplateValidator/TemplatePlaceholders — a test-only copy would drift.</summary>
public static class StarterTemplate
{
    public const string Html = """
        <!-- BLOCK: shell -->
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>{{issue_title}}</title></head>
        <body style="margin:0;background:#EDEEF3;">
          <div style="display:none;max-height:0;overflow:hidden;">{{preheader}}</div>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:24px 12px;">
          <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;">
          {{sections}}
          <tr><td style="padding:24px;font:12px sans-serif;color:#888;">
            <a href="{{unsubscribe_url}}" style="color:#888;">Unsubscribe</a>
          </td></tr>
          </table></td></tr></table>
        </body></html>
        <!-- /BLOCK -->

        <!-- BLOCK: header -->
        <tr><td style="padding:24px 24px 0;font:700 28px sans-serif;">{{title}}</td></tr>
        <tr><td style="padding:8px 24px 0;font:16px/1.6 sans-serif;">{{body_html}}</td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: topic -->
        <tr><td style="padding:24px 24px 0;">
          <!-- IF: image --><img src="{{image_url}}" alt="{{title}}" width="536" style="display:block;width:100%;max-width:536px;height:auto;" /><!-- /IF -->
          <!-- IF: category --><p style="margin:12px 0 0;font:11px monospace;color:#8A8FA0;text-transform:uppercase;">{{category}} &middot; {{reading_time}}</p><!-- /IF -->
          <p style="margin:8px 0 0;font:700 21px sans-serif;">{{title}}</p>
          <div style="font:15px/1.6 sans-serif;color:#5A5E70;">{{body_html}}</div>
          <!-- IF: link --><p style="margin:10px 0 0;"><a href="{{link_url}}" style="color:{{accent}};font:600 14px sans-serif;text-decoration:none;">{{link_text}}</a></p><!-- /IF -->
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: video -->
        <tr><td style="padding:24px 24px 0;">
          <!-- IF: thumbnail --><img src="{{thumbnail_url}}" alt="{{title}}" width="536" style="display:block;width:100%;max-width:536px;height:auto;" /><!-- /IF -->
          <p style="margin:12px 0 0;font:700 18px sans-serif;">{{title}}</p>
          <div style="font:14px/1.6 sans-serif;color:#5A5E70;">{{body_html}}</div>
          <!-- IF: video --><p style="margin:10px 0 0;"><a href="{{video_url}}" style="color:{{accent}};font:600 14px sans-serif;text-decoration:none;">{{link_text}}</a></p><!-- /IF -->
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: sponsor -->
        <tr><td style="padding:24px 24px 0;">
          <table role="presentation" width="100%" style="background:#F6F7FA;border:1px solid #E4E6ED;"><tr><td style="padding:20px;">
            <p style="margin:0;font:700 10px monospace;letter-spacing:1.1px;color:#5A5E70;">ADVERTISEMENT</p>
            <p style="margin:12px 0 0;font:700 18px sans-serif;">{{title}}</p>
            <div style="font:15px/1.6 sans-serif;color:#5A5E70;">{{body_html}}</div>
            <!-- IF: link --><p style="margin:10px 0 0;"><a href="{{link_url}}" style="color:{{accent}};text-decoration:none;">{{link_text}}</a></p><!-- /IF -->
            <p style="margin:14px 0 0;font:italic 12px sans-serif;color:#8A8FA0;">This section is paid promotion.</p>
          </td></tr></table>
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: button -->
        <tr><td style="padding:24px 24px 0;">
          <table role="presentation" cellpadding="0" cellspacing="0"><tr>
            <td align="center" bgcolor="{{accent}}" style="border-radius:8px;">
              <!-- IF: link --><a href="{{link_url}}" style="display:inline-block;padding:12px 24px;font:700 14px sans-serif;color:#090915;text-decoration:none;">{{link_text}}</a><!-- /IF -->
            </td>
          </tr></table>
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: divider -->
        <tr><td style="padding:24px 24px 0;"><table role="presentation" width="100%"><tr><td style="border-top:1px solid #E4E6ED;font-size:0;line-height:0;">&nbsp;</td></tr></table></td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: footer -->
        <tr><td style="padding:24px;font:12px/1.6 sans-serif;color:#6B7085;">
          <div>{{body_html}}</div>
          <p style="margin:12px 0 0;">{{sender_identity}}</p>
        </td></tr>
        <!-- /BLOCK -->
        """;
}
