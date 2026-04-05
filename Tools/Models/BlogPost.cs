using System;
using System.Collections.Generic;

namespace BlogTools.Models
{
    public class BlogPost
    {
        public string Title { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public DateTime? LastModifiedAt { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public string? Author { get; set; }
        public bool Math { get; set; }
        public bool Toc { get; set; } = true;
        public bool Pin { get; set; }
        public string? Description { get; set; }
        public string? Image { get; set; }
        public string Content { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }
}
