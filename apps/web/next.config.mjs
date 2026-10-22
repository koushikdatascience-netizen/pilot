const nextConfig = {
  output: "standalone",
  async rewrites() {
    return [{ source: "/api/v1/:path*", destination: "http://localhost:5088/api/v1/:path*" }];
  }
};

export default nextConfig;
