using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ForgeCMETool.Controllers
{
    public class jsTreeNode
    {
        public jsTreeNode(string id, string text, string type, bool children)
        {
            this.id = id;
            this.text = text;
            this.type = type;
            this.children = children;
        }

        public string id { get; set; }
        public string text { get; set; }
        public string type { get; set; }
        public bool children { get; set; }
    }
}
