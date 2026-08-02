#!/bin/sh
set -eu

if [ -z "${AVEON_PUBLIC_URL:-}" ]; then
  echo "AVEON_PUBLIC_URL must be set when starting the container." >&2
  exit 1
fi

public_url=${AVEON_PUBLIC_URL%/}
case "$public_url" in
  http://*|https://*) ;;
  *)
    echo "AVEON_PUBLIC_URL must be an absolute HTTP or HTTPS URL." >&2
    exit 1
    ;;
esac

authority=${public_url#*://}
case "$authority" in
  ""|*/*)
    echo "AVEON_PUBLIC_URL must contain only an origin, without a path." >&2
    exit 1
    ;;
esac

web_root=${AVEON_WEB_ROOT:-/app/wwwroot}
escaped_url=$(printf '%s' "$public_url" | sed 's/[&|\\]/\\&/g')
sed "s|__AVEON_PUBLIC_URL__|$escaped_url|g" "$web_root/index.template.html" > "$web_root/index.html"

cat > "$web_root/robots.txt" <<EOF
User-agent: *
Allow: /

Sitemap: $public_url/sitemap.xml
EOF

cat > "$web_root/sitemap.xml" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>$public_url/</loc>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
  <url>
    <loc>$public_url/search</loc>
    <changefreq>weekly</changefreq>
    <priority>1.0</priority>
  </url>
  <url>
    <loc>$public_url/explore</loc>
    <changefreq>weekly</changefreq>
    <priority>0.9</priority>
  </url>
  <url>
    <loc>$public_url/about</loc>
    <changefreq>monthly</changefreq>
    <priority>0.7</priority>
  </url>
  <url>
    <loc>$public_url/how-it-works</loc>
    <changefreq>monthly</changefreq>
    <priority>0.8</priority>
  </url>
  <url>
    <loc>$public_url/multi-destination</loc>
    <changefreq>weekly</changefreq>
    <priority>0.9</priority>
  </url>
</urlset>
EOF

exec "$@"
