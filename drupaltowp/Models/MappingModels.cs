using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace drupaltowp.Models;

public class CategoryMappinModel {
    public int DrupalCategoryId { get; set; }
    public int WordPressCategoryId { get; set; }
    public string vocabulary { get; set; } = "default"; // Default vocabulary name
}