using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BlogTools.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BlogTools.Services
{
    public class JekyllService
    {
        private readonly string _blogPath;
        private readonly IDeserializer _deserializer;
        private readonly ISerializer _serializer;

        public string BlogPath => _blogPath;

        public JekyllService(string blogPath)
        {
            _blogPath = blogPath;
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            
            _serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance) // Jekyll often uses lowercase/camel, but let's just make sure it serializes properly.
                .Build();
        }

        public Dictionary<string, object> LoadConfig()
        {
            var configPath = Path.Combine(_blogPath, "_config.yml");
            if (!File.Exists(configPath)) return new Dictionary<string, object>();
            
            var yaml = File.ReadAllText(configPath);
            return _deserializer.Deserialize<Dictionary<string, object>>(yaml) ?? new Dictionary<string, object>();
        }

        public void SaveConfig(Dictionary<string, object> config)
        {
            var configPath = Path.Combine(_blogPath, "_config.yml");
            var yaml = _serializer.Serialize(config);
            File.WriteAllText(configPath, yaml);
        }

        public void SavePost(BlogPost post)
        {
            var fileName = post.FileName;
            var isNewPost = string.IsNullOrEmpty(fileName);
            if (isNewPost)
            {
                // Generate a friendly filename from the title (supports Chinese and other Unicode)
                var slug = Regex.Replace(post.Title, @"[\\/:*?""<>|]+", "-").Trim('-', ' ');
                slug = Regex.Replace(slug, @"\s+", "-"); // collapse whitespace to dashes
                if (string.IsNullOrWhiteSpace(slug)) slug = "untitled";
                fileName = $"{post.Date:yyyy-MM-dd}-{slug}.md";
                post.FileName = fileName;
            }

            var postsPath = Path.Combine(_blogPath, "_posts");
            Directory.CreateDirectory(postsPath);
            var filePath = Path.Combine(postsPath, fileName);
            
            // If the title changed drastically, we might want to keep the old filename, or rename it. 
            // Here we just use the original FileName to overwrite.
            post.FullPath = filePath;

            // Prepare Front Matter dictionary
            var frontMatter = new Dictionary<string, object>
            {
                { "title", post.Title },
                { "date", FormatJekyllDate(post.Date) }
            };

            if (post.LastModifiedAt.HasValue)
                frontMatter["last_modified_at"] = FormatJekyllDate(post.LastModifiedAt.Value);

            if (post.Categories != null && post.Categories.Count > 0)
                frontMatter["categories"] = post.Categories;

            if (post.Tags != null && post.Tags.Count > 0)
                frontMatter["tags"] = post.Tags;

            if (!string.IsNullOrWhiteSpace(post.Author))
                frontMatter["author"] = post.Author;

            if (post.Math)
                frontMatter["math"] = true;

            if (!post.Toc) // Default is true in chirp, so we only write if false or if you prefer explicit
                frontMatter["toc"] = false;

            if (post.Pin)
                frontMatter["pin"] = true;

            if (!string.IsNullOrWhiteSpace(post.Description))
                frontMatter["description"] = post.Description;

            if (!string.IsNullOrWhiteSpace(post.Image))
                frontMatter["image"] = post.Image;

            var yaml = _serializer.Serialize(frontMatter);
            var fileContent = $"---\n{yaml}---\n\n{post.Content}";
            
            File.WriteAllText(filePath, fileContent);
        }

        public List<BlogPost> GetAllPosts()
        {
            var postsDir = Path.Combine(_blogPath, "_posts");
            if (!Directory.Exists(postsDir)) return new List<BlogPost>();
            
            var list = new List<BlogPost>();
            var files = Directory.GetFiles(postsDir, "*.md");
            foreach (var file in files)
            {
                var post = ParsePost(file);
                if (post != null) list.Add(post);
            }
            // Sort to latest posts first
            list.Sort((a, b) => b.Date.CompareTo(a.Date));
            return list;
        }

        public BlogPost? ParsePost(string filePath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var post = new BlogPost
                {
                    FullPath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Content = content
                };
                
                if (content.StartsWith("---"))
                {
                    var endOfFm = content.IndexOf("---", 3);
                    if (endOfFm > 0)
                    {
                        var yaml = content.Substring(3, endOfFm - 3);
                        post.Content = content.Substring(endOfFm + 3).TrimStart();
                        
                        var fm = _deserializer.Deserialize<Dictionary<string, object>>(yaml);
                        if (fm != null)
                        {
                            if (fm.TryGetValue("title", out var title)) post.Title = title?.ToString() ?? "";
                            if (fm.TryGetValue("date", out var date) && TryParseJekyllDate(date?.ToString(), out var parsedDate)) post.Date = parsedDate;
                            if (fm.TryGetValue("last_modified_at", out var lm) && TryParseJekyllDate(lm?.ToString(), out var parsedLm)) post.LastModifiedAt = parsedLm;

                            if (fm.TryGetValue("author", out var author)) post.Author = author?.ToString();
                            if (fm.TryGetValue("description", out var desc)) post.Description = desc?.ToString();
                            if (fm.TryGetValue("image", out var img)) post.Image = img?.ToString();

                            // Boolean fields
                            if (fm.TryGetValue("math", out var math)) post.Math = TryParseBool(math);
                            if (fm.TryGetValue("toc", out var toc)) post.Toc = TryParseBool(toc);
                            if (fm.TryGetValue("pin", out var pin)) post.Pin = TryParseBool(pin);
                            
                            if (fm.TryGetValue("categories", out var cats))
                                post.Categories = ToStringList(cats);
                            
                            if (fm.TryGetValue("tags", out var tags))
                                post.Tags = ToStringList(tags);
                        }
                    }
                }
                return post;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse post '{filePath}': {ex.Message}");
                return null;
            }
        }

        public void DeletePost(string filePath)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        private string FormatJekyllDate(DateTime dt)
        {
            // Chirpy format: 2025-09-05 17:00:00 +0800
            // zzz gives +08:00
            var offset = dt.ToString("zzz");
            var cleanOffset = offset.Replace(":", "");
            return dt.ToString("yyyy-MM-dd HH:mm:ss") + " " + cleanOffset;
        }

        private bool TryParseJekyllDate(string? dateStr, out DateTime result)
        {
            result = DateTime.Now;
            if (string.IsNullOrWhiteSpace(dateStr)) return false;

            var normalized = Regex.Replace(dateStr.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
            if (DateTimeOffset.TryParse(normalized, out var offsetDate))
            {
                result = offsetDate.DateTime;
                return true;
            }

            return DateTime.TryParse(normalized, out result);
        }

        private bool TryParseBool(object? value)
        {
            if (value == null) return false;
            if (value is bool b) return b;
            if (bool.TryParse(value.ToString(), out var res)) return res;
            return false;
        }

        private static List<string> ToStringList(object? value)
        {
            if (value is IEnumerable<object> items)
            {
                return items
                    .Select(item => item?.ToString()?.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Cast<string>()
                    .ToList();
            }

            if (value is string text)
            {
                return text
                    .Split(new[] { ',', '\uFF0C', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();
            }

            return new List<string>();
        }
    }
}
