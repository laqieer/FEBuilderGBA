// GENERATED FILE, DO NOT MODIFY");


uint32_t CompressionNative_Crc32 (uint32_t, void *, int32_t);

int32_t CompressionNative_Deflate (void *, int32_t);

int32_t CompressionNative_DeflateEnd (void *);

int32_t CompressionNative_DeflateInit2_ (void *, int32_t, int32_t, int32_t, int32_t, int32_t);

int32_t CompressionNative_Inflate (void *, int32_t);

int32_t CompressionNative_InflateEnd (void *);

int32_t CompressionNative_InflateInit2_ (void *, int32_t);

void * eglGetProcAddress (void *);

void GlobalizationNative_ChangeCase (void *, int32_t, void *, int32_t, int32_t);

void GlobalizationNative_ChangeCaseInvariant (void *, int32_t, void *, int32_t, int32_t);

void GlobalizationNative_ChangeCaseTurkish (void *, int32_t, void *, int32_t, int32_t);

void GlobalizationNative_CloseSortHandle (void *);

int32_t GlobalizationNative_CompareString (void *, void *, int32_t, void *, int32_t, int32_t);

int32_t GlobalizationNative_EndsWith (void *, void *, int32_t, void *, int32_t, int32_t, void *);

int32_t GlobalizationNative_EnumCalendarInfo (void *, void *, uint32_t, int32_t, void *);

int32_t GlobalizationNative_GetCalendarInfo (void *, uint32_t, int32_t, void *, int32_t);

int32_t GlobalizationNative_GetCalendars (void *, void *, int32_t);

int32_t GlobalizationNative_GetDefaultLocaleName (void *, int32_t);

int32_t GlobalizationNative_GetICUVersion ();

int32_t GlobalizationNative_GetJapaneseEraStartDate (int32_t, void *, void *, void *);

int32_t GlobalizationNative_GetLatestJapaneseEra ();

int32_t GlobalizationNative_GetLocaleInfoGroupingSizes (void *, uint32_t, void *, void *);

int32_t GlobalizationNative_GetLocaleInfoInt (void *, uint32_t, void *);

int32_t GlobalizationNative_GetLocaleInfoString (void *, uint32_t, void *, int32_t, void *);

int32_t GlobalizationNative_GetLocaleName (void *, void *, int32_t);

int32_t GlobalizationNative_GetLocales (void *, int32_t);

int32_t GlobalizationNative_GetLocaleTimeFormat (void *, int32_t, void *, int32_t);

int32_t GlobalizationNative_GetSortHandle (void *, void *);

int32_t GlobalizationNative_GetSortKey (void *, void *, int32_t, void *, int32_t, int32_t);

int32_t GlobalizationNative_GetSortVersion (void *);

int32_t GlobalizationNative_IndexOf (void *, void *, int32_t, void *, int32_t, int32_t, void *);

void GlobalizationNative_InitICUFunctions (void *, void *, void *, void *);

void GlobalizationNative_InitOrdinalCasingPage (int32_t, void *);

int32_t GlobalizationNative_IsNormalized (int32_t, void *, int32_t);

int32_t GlobalizationNative_IsPredefinedLocale (void *);

int32_t GlobalizationNative_LastIndexOf (void *, void *, int32_t, void *, int32_t, int32_t, void *);

int32_t GlobalizationNative_LoadICU ();

int32_t GlobalizationNative_NormalizeString (int32_t, void *, int32_t, void *, int32_t);

int32_t GlobalizationNative_StartsWith (void *, void *, int32_t, void *, int32_t, int32_t, void *);

int32_t GlobalizationNative_ToAscii (uint32_t, void *, int32_t, void *, int32_t);

int32_t GlobalizationNative_ToUnicode (uint32_t, void *, int32_t, void *, int32_t);

void gr_backendrendertarget_delete (void *);

int32_t gr_backendrendertarget_get_backend (void *);

int32_t gr_backendrendertarget_get_gl_framebufferinfo (void *, void *);

int32_t gr_backendrendertarget_get_height (void *);

int32_t gr_backendrendertarget_get_samples (void *);

int32_t gr_backendrendertarget_get_stencils (void *);

int32_t gr_backendrendertarget_get_width (void *);

int32_t gr_backendrendertarget_is_valid (void *);

void * gr_backendrendertarget_new_gl (int32_t, int32_t, int32_t, int32_t, void *);

void * gr_backendrendertarget_new_metal (int32_t, int32_t, int32_t, void *);

void * gr_backendrendertarget_new_vulkan (int32_t, int32_t, int32_t, void *);

void gr_backendtexture_delete (void *);

int32_t gr_backendtexture_get_backend (void *);

int32_t gr_backendtexture_get_gl_textureinfo (void *, void *);

int32_t gr_backendtexture_get_height (void *);

int32_t gr_backendtexture_get_width (void *);

int32_t gr_backendtexture_has_mipmaps (void *);

int32_t gr_backendtexture_is_valid (void *);

void * gr_backendtexture_new_gl (int32_t, int32_t, int32_t, void *);

void * gr_backendtexture_new_metal (int32_t, int32_t, int32_t, void *);

void * gr_backendtexture_new_vulkan (int32_t, int32_t, void *);

void gr_direct_context_abandon_context (void *);

void gr_direct_context_dump_memory_statistics (void *, void *);

void gr_direct_context_flush (void *);

void gr_direct_context_flush_and_submit (void *, int32_t);

void gr_direct_context_free_gpu_resources (void *);

void * gr_direct_context_get_resource_cache_limit (void *);

void gr_direct_context_get_resource_cache_usage (void *, void *, void *);

int32_t gr_direct_context_is_abandoned (void *);

void * gr_direct_context_make_gl (void *);

void * gr_direct_context_make_gl_with_options (void *, void *);

void * gr_direct_context_make_metal (void *, void *);

void * gr_direct_context_make_metal_with_options (void *, void *, void *);

void * gr_direct_context_make_vulkan (void *);

void * gr_direct_context_make_vulkan_with_options (void *, void *);

void gr_direct_context_perform_deferred_cleanup (void *, int64_t);

void gr_direct_context_purge_unlocked_resources (void *, int32_t);

void gr_direct_context_purge_unlocked_resources_bytes (void *, void *, int32_t);

void gr_direct_context_release_resources_and_abandon_context (void *);

void gr_direct_context_reset_context (void *, uint32_t);

void gr_direct_context_set_resource_cache_limit (void *, void *);

int32_t gr_direct_context_submit (void *, int32_t);

void * gr_glinterface_assemble_gl_interface (void *, void *);

void * gr_glinterface_assemble_gles_interface (void *, void *);

void * gr_glinterface_assemble_interface (void *, void *);

void * gr_glinterface_assemble_webgl_interface (void *, void *);

void * gr_glinterface_create_native_interface ();

int32_t gr_glinterface_has_extension (void *, void *);

void gr_glinterface_unref (void *);

int32_t gr_glinterface_validate (void *);

int32_t gr_recording_context_get_backend (void *);

int32_t gr_recording_context_get_max_surface_sample_count_for_color_type (void *, int32_t);

void gr_recording_context_unref (void *);

void gr_vk_extensions_delete (void *);

int32_t gr_vk_extensions_has_extension (void *, void *, uint32_t);

void gr_vk_extensions_init (void *, void *, void *, void *, void *, uint32_t, void *, uint32_t, void *);

void * gr_vk_extensions_new ();

void * hb_blob_copy_writable_or_fail (void *);

void * hb_blob_create (void *, uint32_t, int32_t, void *, void *);

void * hb_blob_create_from_file (void *);

void * hb_blob_create_from_file_or_fail (void *);

void * hb_blob_create_or_fail (void *, uint32_t, int32_t, void *, void *);

void * hb_blob_create_sub_blob (void *, uint32_t, uint32_t);

void hb_blob_destroy (void *);

void * hb_blob_get_data (void *, void *);

void * hb_blob_get_data_writable (void *, void *);

void * hb_blob_get_empty ();

uint32_t hb_blob_get_length (void *);

int32_t hb_blob_is_immutable (void *);

void hb_blob_make_immutable (void *);

void * hb_blob_reference (void *);

void hb_buffer_add (void *, uint32_t, uint32_t);

void hb_buffer_add_codepoints (void *, void *, int32_t, uint32_t, int32_t);

void hb_buffer_add_latin1 (void *, void *, int32_t, uint32_t, int32_t);

void hb_buffer_add_utf16 (void *, void *, int32_t, uint32_t, int32_t);

void hb_buffer_add_utf32 (void *, void *, int32_t, uint32_t, int32_t);

void hb_buffer_add_utf8 (void *, void *, int32_t, uint32_t, int32_t);

int32_t hb_buffer_allocation_successful (void *);

void hb_buffer_append (void *, void *, uint32_t, uint32_t);

void hb_buffer_clear_contents (void *);

void * hb_buffer_create ();

int32_t hb_buffer_deserialize_glyphs (void *, void *, int32_t, void *, void *, int32_t);

int32_t hb_buffer_deserialize_unicode (void *, void *, int32_t, void *, int32_t);

void hb_buffer_destroy (void *);

int32_t hb_buffer_diff (void *, void *, uint32_t, uint32_t);

int32_t hb_buffer_get_cluster_level (void *);

int32_t hb_buffer_get_content_type (void *);

int32_t hb_buffer_get_direction (void *);

void * hb_buffer_get_empty ();

int32_t hb_buffer_get_flags (void *);

void * hb_buffer_get_glyph_infos (void *, void *);

void * hb_buffer_get_glyph_positions (void *, void *);

uint32_t hb_buffer_get_invisible_glyph (void *);

void * hb_buffer_get_language (void *);

uint32_t hb_buffer_get_length (void *);

uint32_t hb_buffer_get_replacement_codepoint (void *);

uint32_t hb_buffer_get_script (void *);

void * hb_buffer_get_unicode_funcs (void *);

void hb_buffer_guess_segment_properties (void *);

int32_t hb_buffer_has_positions (void *);

void hb_buffer_normalize_glyphs (void *);

int32_t hb_buffer_pre_allocate (void *, uint32_t);

void * hb_buffer_reference (void *);

void hb_buffer_reset (void *);

void hb_buffer_reverse (void *);

void hb_buffer_reverse_clusters (void *);

void hb_buffer_reverse_range (void *, uint32_t, uint32_t);

uint32_t hb_buffer_serialize (void *, uint32_t, uint32_t, void *, uint32_t, void *, void *, int32_t, int32_t);

int32_t hb_buffer_serialize_format_from_string (void *, int32_t);

void * hb_buffer_serialize_format_to_string (int32_t);

uint32_t hb_buffer_serialize_glyphs (void *, uint32_t, uint32_t, void *, uint32_t, void *, void *, int32_t, int32_t);

void * hb_buffer_serialize_list_formats ();

uint32_t hb_buffer_serialize_unicode (void *, uint32_t, uint32_t, void *, uint32_t, void *, int32_t, int32_t);

void hb_buffer_set_cluster_level (void *, int32_t);

void hb_buffer_set_content_type (void *, int32_t);

void hb_buffer_set_direction (void *, int32_t);

void hb_buffer_set_flags (void *, int32_t);

void hb_buffer_set_invisible_glyph (void *, uint32_t);

void hb_buffer_set_language (void *, void *);

int32_t hb_buffer_set_length (void *, uint32_t);

void hb_buffer_set_message_func (void *, void *, void *, void *);

void hb_buffer_set_replacement_codepoint (void *, uint32_t);

void hb_buffer_set_script (void *, uint32_t);

void hb_buffer_set_unicode_funcs (void *, void *);

uint32_t hb_color_get_alpha (uint32_t);

uint32_t hb_color_get_blue (uint32_t);

uint32_t hb_color_get_green (uint32_t);

uint32_t hb_color_get_red (uint32_t);

int32_t hb_direction_from_string (void *, int32_t);

void * hb_direction_to_string (int32_t);

int32_t hb_face_builder_add_table (void *, uint32_t, void *);

void * hb_face_builder_create ();

void hb_face_collect_unicodes (void *, void *);

void hb_face_collect_variation_selectors (void *, void *);

void hb_face_collect_variation_unicodes (void *, uint32_t, void *);

uint32_t hb_face_count (void *);

void * hb_face_create (void *, uint32_t);

void * hb_face_create_for_tables (void *, void *, void *);

void hb_face_destroy (void *);

void * hb_face_get_empty ();

uint32_t hb_face_get_glyph_count (void *);

uint32_t hb_face_get_index (void *);

uint32_t hb_face_get_table_tags (void *, uint32_t, void *, void *);

uint32_t hb_face_get_upem (void *);

int32_t hb_face_is_immutable (void *);

void hb_face_make_immutable (void *);

void * hb_face_reference (void *);

void * hb_face_reference_blob (void *);

void * hb_face_reference_table (void *, uint32_t);

void hb_face_set_glyph_count (void *, uint32_t);

void hb_face_set_index (void *, uint32_t);

void hb_face_set_upem (void *, uint32_t);

int32_t hb_feature_from_string (void *, int32_t, void *);

void hb_feature_to_string (void *, void *, uint32_t);

void hb_font_add_glyph_origin_for_direction (void *, uint32_t, int32_t, void *, void *);

void * hb_font_create (void *);

void * hb_font_create_sub_font (void *);

void hb_font_destroy (void *);

void * hb_font_funcs_create ();

void hb_font_funcs_destroy (void *);

void * hb_font_funcs_get_empty ();

int32_t hb_font_funcs_is_immutable (void *);

void hb_font_funcs_make_immutable (void *);

void * hb_font_funcs_reference (void *);

void hb_font_funcs_set_font_h_extents_func (void *, void *, void *, void *);

void hb_font_funcs_set_font_v_extents_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_contour_point_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_extents_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_from_name_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_h_advance_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_h_advances_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_h_kerning_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_h_origin_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_name_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_v_advance_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_v_advances_func (void *, void *, void *, void *);

void hb_font_funcs_set_glyph_v_origin_func (void *, void *, void *, void *);

void hb_font_funcs_set_nominal_glyph_func (void *, void *, void *, void *);

void hb_font_funcs_set_nominal_glyphs_func (void *, void *, void *, void *);

void hb_font_funcs_set_variation_glyph_func (void *, void *, void *, void *);

void * hb_font_get_empty ();

void hb_font_get_extents_for_direction (void *, int32_t, void *);

void * hb_font_get_face (void *);

int32_t hb_font_get_glyph (void *, uint32_t, uint32_t, void *);

void hb_font_get_glyph_advance_for_direction (void *, uint32_t, int32_t, void *, void *);

void hb_font_get_glyph_advances_for_direction (void *, int32_t, uint32_t, void *, uint32_t, void *, uint32_t);

int32_t hb_font_get_glyph_contour_point (void *, uint32_t, uint32_t, void *, void *);

int32_t hb_font_get_glyph_contour_point_for_origin (void *, uint32_t, uint32_t, int32_t, void *, void *);

int32_t hb_font_get_glyph_extents (void *, uint32_t, void *);

int32_t hb_font_get_glyph_extents_for_origin (void *, uint32_t, int32_t, void *);

int32_t hb_font_get_glyph_from_name (void *, void *, int32_t, void *);

int32_t hb_font_get_glyph_h_advance (void *, uint32_t);

void hb_font_get_glyph_h_advances (void *, uint32_t, void *, uint32_t, void *, uint32_t);

int32_t hb_font_get_glyph_h_kerning (void *, uint32_t, uint32_t);

int32_t hb_font_get_glyph_h_origin (void *, uint32_t, void *, void *);

void hb_font_get_glyph_kerning_for_direction (void *, uint32_t, uint32_t, int32_t, void *, void *);

int32_t hb_font_get_glyph_name (void *, uint32_t, void *, uint32_t);

void hb_font_get_glyph_origin_for_direction (void *, uint32_t, int32_t, void *, void *);

int32_t hb_font_get_glyph_v_advance (void *, uint32_t);

void hb_font_get_glyph_v_advances (void *, uint32_t, void *, uint32_t, void *, uint32_t);

int32_t hb_font_get_glyph_v_origin (void *, uint32_t, void *, void *);

int32_t hb_font_get_h_extents (void *, void *);

int32_t hb_font_get_nominal_glyph (void *, uint32_t, void *);

uint32_t hb_font_get_nominal_glyphs (void *, uint32_t, void *, uint32_t, void *, uint32_t);

void * hb_font_get_parent (void *);

void hb_font_get_ppem (void *, void *, void *);

float hb_font_get_ptem (void *);

void hb_font_get_scale (void *, void *, void *);

int32_t hb_font_get_v_extents (void *, void *);

void * hb_font_get_var_coords_normalized (void *, void *);

int32_t hb_font_get_variation_glyph (void *, uint32_t, uint32_t, void *);

int32_t hb_font_glyph_from_string (void *, void *, int32_t, void *);

void hb_font_glyph_to_string (void *, uint32_t, void *, uint32_t);

int32_t hb_font_is_immutable (void *);

void hb_font_make_immutable (void *);

void * hb_font_reference (void *);

void hb_font_set_face (void *, void *);

void hb_font_set_funcs (void *, void *, void *, void *);

void hb_font_set_funcs_data (void *, void *, void *);

void hb_font_set_parent (void *, void *);

void hb_font_set_ppem (void *, uint32_t, uint32_t);

void hb_font_set_ptem (void *, float);

void hb_font_set_scale (void *, int32_t, int32_t);

void hb_font_set_var_coords_design (void *, void *, uint32_t);

void hb_font_set_var_coords_normalized (void *, void *, uint32_t);

void hb_font_set_var_named_instance (void *, uint32_t);

void hb_font_set_variations (void *, void *, uint32_t);

void hb_font_subtract_glyph_origin_for_direction (void *, uint32_t, int32_t, void *, void *);

int32_t hb_glyph_info_get_glyph_flags (void *);

void * hb_language_from_string (void *, int32_t);

void * hb_language_get_default ();

void * hb_language_to_string (void *);

int32_t hb_map_allocation_successful (void *);

void hb_map_clear (void *);

void * hb_map_create ();

void hb_map_del (void *, uint32_t);

void hb_map_destroy (void *);

uint32_t hb_map_get (void *, uint32_t);

void * hb_map_get_empty ();

uint32_t hb_map_get_population (void *);

int32_t hb_map_has (void *, uint32_t);

int32_t hb_map_is_empty (void *);

void * hb_map_reference (void *);

void hb_map_set (void *, uint32_t, uint32_t);

uint32_t hb_ot_color_glyph_get_layers (void *, uint32_t, uint32_t, void *, void *);

void * hb_ot_color_glyph_reference_png (void *, uint32_t);

void * hb_ot_color_glyph_reference_svg (void *, uint32_t);

int32_t hb_ot_color_has_layers (void *);

int32_t hb_ot_color_has_palettes (void *);

int32_t hb_ot_color_has_png (void *);

int32_t hb_ot_color_has_svg (void *);

int32_t hb_ot_color_palette_color_get_name_id (void *, uint32_t);

uint32_t hb_ot_color_palette_get_colors (void *, uint32_t, uint32_t, void *, void *);

uint32_t hb_ot_color_palette_get_count (void *);

int32_t hb_ot_color_palette_get_flags (void *, uint32_t);

int32_t hb_ot_color_palette_get_name_id (void *, uint32_t);

void hb_ot_font_set_funcs (void *);

void hb_ot_layout_collect_features (void *, uint32_t, void *, void *, void *, void *);

void hb_ot_layout_collect_lookups (void *, uint32_t, void *, void *, void *, void *);

uint32_t hb_ot_layout_feature_get_characters (void *, uint32_t, uint32_t, uint32_t, void *, void *);

uint32_t hb_ot_layout_feature_get_lookups (void *, uint32_t, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_feature_get_name_ids (void *, uint32_t, uint32_t, void *, void *, void *, void *, void *);

uint32_t hb_ot_layout_feature_with_variations_get_lookups (void *, uint32_t, uint32_t, uint32_t, uint32_t, void *, void *);

uint32_t hb_ot_layout_get_attach_points (void *, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_get_baseline (void *, int32_t, int32_t, uint32_t, uint32_t, void *);

int32_t hb_ot_layout_get_glyph_class (void *, uint32_t);

void hb_ot_layout_get_glyphs_in_class (void *, int32_t, void *);

uint32_t hb_ot_layout_get_ligature_carets (void *, int32_t, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_get_size_params (void *, void *, void *, void *, void *, void *);

int32_t hb_ot_layout_has_glyph_classes (void *);

int32_t hb_ot_layout_has_positioning (void *);

int32_t hb_ot_layout_has_substitution (void *);

int32_t hb_ot_layout_language_find_feature (void *, uint32_t, uint32_t, uint32_t, uint32_t, void *);

uint32_t hb_ot_layout_language_get_feature_indexes (void *, uint32_t, uint32_t, uint32_t, uint32_t, void *, void *);

uint32_t hb_ot_layout_language_get_feature_tags (void *, uint32_t, uint32_t, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_language_get_required_feature (void *, uint32_t, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_language_get_required_feature_index (void *, uint32_t, uint32_t, uint32_t, void *);

void hb_ot_layout_lookup_collect_glyphs (void *, uint32_t, uint32_t, void *, void *, void *, void *);

uint32_t hb_ot_layout_lookup_get_glyph_alternates (void *, uint32_t, uint32_t, uint32_t, void *, void *);

void hb_ot_layout_lookup_substitute_closure (void *, uint32_t, void *);

int32_t hb_ot_layout_lookup_would_substitute (void *, uint32_t, void *, uint32_t, int32_t);

void hb_ot_layout_lookups_substitute_closure (void *, void *, void *);

uint32_t hb_ot_layout_script_get_language_tags (void *, uint32_t, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_script_select_language (void *, uint32_t, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_table_find_feature_variations (void *, uint32_t, void *, uint32_t, void *);

int32_t hb_ot_layout_table_find_script (void *, uint32_t, uint32_t, void *);

uint32_t hb_ot_layout_table_get_feature_tags (void *, uint32_t, uint32_t, void *, void *);

uint32_t hb_ot_layout_table_get_lookup_count (void *, uint32_t);

uint32_t hb_ot_layout_table_get_script_tags (void *, uint32_t, uint32_t, void *, void *);

int32_t hb_ot_layout_table_select_script (void *, uint32_t, uint32_t, void *, void *, void *);

int32_t hb_ot_math_get_constant (void *, int32_t);

uint32_t hb_ot_math_get_glyph_assembly (void *, uint32_t, int32_t, uint32_t, void *, void *, void *);

int32_t hb_ot_math_get_glyph_italics_correction (void *, uint32_t);

int32_t hb_ot_math_get_glyph_kerning (void *, uint32_t, int32_t, int32_t);

int32_t hb_ot_math_get_glyph_top_accent_attachment (void *, uint32_t);

uint32_t hb_ot_math_get_glyph_variants (void *, uint32_t, int32_t, uint32_t, void *, void *);

int32_t hb_ot_math_get_min_connector_overlap (void *, int32_t);

int32_t hb_ot_math_has_data (void *);

int32_t hb_ot_math_is_glyph_extended_shape (void *, uint32_t);

uint32_t hb_ot_meta_get_entry_tags (void *, uint32_t, void *, void *);

void * hb_ot_meta_reference_entry (void *, int32_t);

int32_t hb_ot_metrics_get_position (void *, int32_t, void *);

float hb_ot_metrics_get_variation (void *, int32_t);

int32_t hb_ot_metrics_get_x_variation (void *, int32_t);

int32_t hb_ot_metrics_get_y_variation (void *, int32_t);

uint32_t hb_ot_name_get_utf16 (void *, int32_t, void *, void *, void *);

uint32_t hb_ot_name_get_utf32 (void *, int32_t, void *, void *, void *);

uint32_t hb_ot_name_get_utf8 (void *, int32_t, void *, void *, void *);

void * hb_ot_name_list_names (void *, void *);

void hb_ot_shape_glyphs_closure (void *, void *, void *, uint32_t, void *);

void hb_ot_shape_plan_collect_lookups (void *, uint32_t, void *);

void * hb_ot_tag_to_language (uint32_t);

uint32_t hb_ot_tag_to_script (uint32_t);

void hb_ot_tags_from_script_and_language (uint32_t, void *, void *, void *, void *, void *);

void hb_ot_tags_to_script_and_language (uint32_t, uint32_t, void *, void *);

uint32_t hb_script_from_iso15924_tag (uint32_t);

uint32_t hb_script_from_string (void *, int32_t);

int32_t hb_script_get_horizontal_direction (uint32_t);

uint32_t hb_script_to_iso15924_tag (uint32_t);

void hb_set_add (void *, uint32_t);

void hb_set_add_range (void *, uint32_t, uint32_t);

int32_t hb_set_allocation_successful (void *);

void hb_set_clear (void *);

void * hb_set_copy (void *);

void * hb_set_create ();

void hb_set_del (void *, uint32_t);

void hb_set_del_range (void *, uint32_t, uint32_t);

void hb_set_destroy (void *);

void * hb_set_get_empty ();

uint32_t hb_set_get_max (void *);

uint32_t hb_set_get_min (void *);

uint32_t hb_set_get_population (void *);

int32_t hb_set_has (void *, uint32_t);

void hb_set_intersect (void *, void *);

int32_t hb_set_is_empty (void *);

int32_t hb_set_is_equal (void *, void *);

int32_t hb_set_is_subset (void *, void *);

int32_t hb_set_next (void *, void *);

int32_t hb_set_next_range (void *, void *, void *);

int32_t hb_set_previous (void *, void *);

int32_t hb_set_previous_range (void *, void *, void *);

void * hb_set_reference (void *);

void hb_set_set (void *, void *);

void hb_set_subtract (void *, void *);

void hb_set_symmetric_difference (void *, void *);

void hb_set_union (void *, void *);

void hb_shape (void *, void *, void *, uint32_t);

int32_t hb_shape_full (void *, void *, void *, uint32_t, void *);

void * hb_shape_list_shapers ();

uint32_t hb_tag_from_string (void *, int32_t);

void hb_tag_to_string (uint32_t, void *);

int32_t hb_unicode_combining_class (void *, uint32_t);

int32_t hb_unicode_compose (void *, uint32_t, uint32_t, void *);

int32_t hb_unicode_decompose (void *, uint32_t, void *, void *);

void * hb_unicode_funcs_create (void *);

void hb_unicode_funcs_destroy (void *);

void * hb_unicode_funcs_get_default ();

void * hb_unicode_funcs_get_empty ();

void * hb_unicode_funcs_get_parent (void *);

int32_t hb_unicode_funcs_is_immutable (void *);

void hb_unicode_funcs_make_immutable (void *);

void * hb_unicode_funcs_reference (void *);

void hb_unicode_funcs_set_combining_class_func (void *, void *, void *, void *);

void hb_unicode_funcs_set_compose_func (void *, void *, void *, void *);

void hb_unicode_funcs_set_decompose_func (void *, void *, void *, void *);

void hb_unicode_funcs_set_general_category_func (void *, void *, void *, void *);

void hb_unicode_funcs_set_mirroring_func (void *, void *, void *, void *);

void hb_unicode_funcs_set_script_func (void *, void *, void *, void *);

int32_t hb_unicode_general_category (void *, uint32_t);

uint32_t hb_unicode_mirroring (void *, uint32_t);

uint32_t hb_unicode_script (void *, uint32_t);

int32_t hb_variation_from_string (void *, int32_t, void *);

void hb_variation_to_string (void *, void *, uint32_t);

void hb_version (void *, void *, void *);

int32_t hb_version_atleast (uint32_t, uint32_t, uint32_t);

void * hb_version_string ();

int32_t pthread_self ();

void sk_3dview_apply_to_canvas (void *, void *);

void sk_3dview_destroy (void *);

float sk_3dview_dot_with_normal (void *, float, float, float);

void sk_3dview_get_matrix (void *, void *);

void * sk_3dview_new ();

void sk_3dview_restore (void *);

void sk_3dview_rotate_x_degrees (void *, float);

void sk_3dview_rotate_x_radians (void *, float);

void sk_3dview_rotate_y_degrees (void *, float);

void sk_3dview_rotate_y_radians (void *, float);

void sk_3dview_rotate_z_degrees (void *, float);

void sk_3dview_rotate_z_radians (void *, float);

void sk_3dview_save (void *);

void sk_3dview_translate (void *, float, float, float);

void sk_bitmap_destructor (void *);

void sk_bitmap_erase (void *, uint32_t);

void sk_bitmap_erase_rect (void *, uint32_t, void *);

int32_t sk_bitmap_extract_alpha (void *, void *, void *, void *);

int32_t sk_bitmap_extract_subset (void *, void *, void *);

void * sk_bitmap_get_addr (void *, int32_t, int32_t);

void * sk_bitmap_get_addr_16 (void *, int32_t, int32_t);

void * sk_bitmap_get_addr_32 (void *, int32_t, int32_t);

void * sk_bitmap_get_addr_8 (void *, int32_t, int32_t);

void * sk_bitmap_get_byte_count (void *);

void sk_bitmap_get_info (void *, void *);

uint32_t sk_bitmap_get_pixel_color (void *, int32_t, int32_t);

void sk_bitmap_get_pixel_colors (void *, void *);

void * sk_bitmap_get_pixels (void *, void *);

void * sk_bitmap_get_row_bytes (void *);

int32_t sk_bitmap_install_mask_pixels (void *, void *);

int32_t sk_bitmap_install_pixels (void *, void *, void *, void *, void *, void *);

int32_t sk_bitmap_install_pixels_with_pixmap (void *, void *);

int32_t sk_bitmap_is_immutable (void *);

int32_t sk_bitmap_is_null (void *);

void * sk_bitmap_make_shader (void *, int32_t, int32_t, void *);

void * sk_bitmap_new ();

void sk_bitmap_notify_pixels_changed (void *);

int32_t sk_bitmap_peek_pixels (void *, void *);

int32_t sk_bitmap_ready_to_draw (void *);

void sk_bitmap_reset (void *);

void sk_bitmap_set_immutable (void *);

void sk_bitmap_set_pixels (void *, void *);

void sk_bitmap_swap (void *, void *);

int32_t sk_bitmap_try_alloc_pixels (void *, void *, void *);

int32_t sk_bitmap_try_alloc_pixels_with_flags (void *, void *, uint32_t);

void sk_canvas_clear (void *, uint32_t);

void sk_canvas_clear_color4f (void *, void *);

void sk_canvas_clip_path_with_operation (void *, void *, int32_t, int32_t);

void sk_canvas_clip_rect_with_operation (void *, void *, int32_t, int32_t);

void sk_canvas_clip_region (void *, void *, int32_t);

void sk_canvas_clip_rrect_with_operation (void *, void *, int32_t, int32_t);

void sk_canvas_concat (void *, void *);

void sk_canvas_destroy (void *);

void sk_canvas_discard (void *);

void sk_canvas_draw_annotation (void *, void *, void *, void *);

void sk_canvas_draw_arc (void *, void *, float, float, int32_t, void *);

void sk_canvas_draw_atlas (void *, void *, void *, void *, void *, int32_t, int32_t, void *, void *);

void sk_canvas_draw_circle (void *, float, float, float, void *);

void sk_canvas_draw_color (void *, uint32_t, int32_t);

void sk_canvas_draw_color4f (void *, void *, int32_t);

void sk_canvas_draw_drawable (void *, void *, void *);

void sk_canvas_draw_drrect (void *, void *, void *, void *);

void sk_canvas_draw_image (void *, void *, float, float, void *);

void sk_canvas_draw_image_lattice (void *, void *, void *, void *, void *);

void sk_canvas_draw_image_nine (void *, void *, void *, void *, void *);

void sk_canvas_draw_image_rect (void *, void *, void *, void *, void *);

void sk_canvas_draw_line (void *, float, float, float, float, void *);

void sk_canvas_draw_link_destination_annotation (void *, void *, void *);

void sk_canvas_draw_named_destination_annotation (void *, void *, void *);

void sk_canvas_draw_oval (void *, void *, void *);

void sk_canvas_draw_paint (void *, void *);

void sk_canvas_draw_patch (void *, void *, void *, void *, int32_t, void *);

void sk_canvas_draw_path (void *, void *, void *);

void sk_canvas_draw_picture (void *, void *, void *, void *);

void sk_canvas_draw_point (void *, float, float, void *);

void sk_canvas_draw_points (void *, int32_t, void *, void *, void *);

void sk_canvas_draw_rect (void *, void *, void *);

void sk_canvas_draw_region (void *, void *, void *);

void sk_canvas_draw_round_rect (void *, void *, float, float, void *);

void sk_canvas_draw_rrect (void *, void *, void *);

void sk_canvas_draw_simple_text (void *, void *, void *, int32_t, float, float, void *, void *);

void sk_canvas_draw_text_blob (void *, void *, float, float, void *);

void sk_canvas_draw_url_annotation (void *, void *, void *);

void sk_canvas_draw_vertices (void *, void *, int32_t, void *);

void sk_canvas_flush (void *);

int32_t sk_canvas_get_device_clip_bounds (void *, void *);

int32_t sk_canvas_get_local_clip_bounds (void *, void *);

int32_t sk_canvas_get_save_count (void *);

void sk_canvas_get_total_matrix (void *, void *);

int32_t sk_canvas_is_clip_empty (void *);

int32_t sk_canvas_is_clip_rect (void *);

void * sk_canvas_new_from_bitmap (void *);

int32_t sk_canvas_quick_reject (void *, void *);

void sk_canvas_reset_matrix (void *);

void sk_canvas_restore (void *);

void sk_canvas_restore_to_count (void *, int32_t);

void sk_canvas_rotate_degrees (void *, float);

void sk_canvas_rotate_radians (void *, float);

int32_t sk_canvas_save (void *);

int32_t sk_canvas_save_layer (void *, void *, void *);

void sk_canvas_scale (void *, float, float);

void sk_canvas_set_matrix (void *, void *);

void sk_canvas_skew (void *, float, float);

void sk_canvas_translate (void *, float, float);

void sk_codec_destroy (void *);

int32_t sk_codec_get_encoded_format (void *);

int32_t sk_codec_get_frame_count (void *);

void sk_codec_get_frame_info (void *, void *);

int32_t sk_codec_get_frame_info_for_index (void *, int32_t, void *);

void sk_codec_get_info (void *, void *);

int32_t sk_codec_get_origin (void *);

int32_t sk_codec_get_pixels (void *, void *, void *, void *, void *);

int32_t sk_codec_get_repetition_count (void *);

void sk_codec_get_scaled_dimensions (void *, float, void *);

int32_t sk_codec_get_scanline_order (void *);

int32_t sk_codec_get_scanlines (void *, void *, int32_t, void *);

int32_t sk_codec_get_valid_subset (void *, void *);

int32_t sk_codec_incremental_decode (void *, void *);

void * sk_codec_min_buffered_bytes_needed ();

void * sk_codec_new_from_data (void *);

void * sk_codec_new_from_stream (void *, void *);

int32_t sk_codec_next_scanline (void *);

int32_t sk_codec_output_scanline (void *, int32_t);

int32_t sk_codec_skip_scanlines (void *, int32_t);

int32_t sk_codec_start_incremental_decode (void *, void *, void *, void *, void *);

int32_t sk_codec_start_scanline_decode (void *, void *, void *);

void sk_color_get_bit_shift (void *, void *, void *, void *);

uint32_t sk_color_premultiply (uint32_t);

void sk_color_premultiply_array (void *, int32_t, void *);

uint32_t sk_color_unpremultiply (uint32_t);

void sk_color_unpremultiply_array (void *, int32_t, void *);

void sk_color4f_from_color (uint32_t, void *);

uint32_t sk_color4f_to_color (void *);

void * sk_colorfilter_new_color_matrix (void *);

void * sk_colorfilter_new_compose (void *, void *);

void * sk_colorfilter_new_high_contrast (void *);

void * sk_colorfilter_new_lighting (uint32_t, uint32_t);

void * sk_colorfilter_new_luma_color ();

void * sk_colorfilter_new_mode (uint32_t, int32_t);

void * sk_colorfilter_new_table (void *);

void * sk_colorfilter_new_table_argb (void *, void *, void *, void *);

void sk_colorfilter_unref (void *);

int32_t sk_colorspace_equals (void *, void *);

int32_t sk_colorspace_gamma_close_to_srgb (void *);

int32_t sk_colorspace_gamma_is_linear (void *);

void sk_colorspace_icc_profile_delete (void *);

void * sk_colorspace_icc_profile_get_buffer (void *, void *);

int32_t sk_colorspace_icc_profile_get_to_xyzd50 (void *, void *);

void * sk_colorspace_icc_profile_new ();

int32_t sk_colorspace_icc_profile_parse (void *, void *, void *);

int32_t sk_colorspace_is_numerical_transfer_fn (void *, void *);

int32_t sk_colorspace_is_srgb (void *);

void * sk_colorspace_make_linear_gamma (void *);

void * sk_colorspace_make_srgb_gamma (void *);

void * sk_colorspace_new_icc (void *);

void * sk_colorspace_new_rgb (void *, void *);

void * sk_colorspace_new_srgb ();

void * sk_colorspace_new_srgb_linear ();

int32_t sk_colorspace_primaries_to_xyzd50 (void *, void *);

void sk_colorspace_ref (void *);

void sk_colorspace_to_profile (void *, void *);

int32_t sk_colorspace_to_xyzd50 (void *, void *);

float sk_colorspace_transfer_fn_eval (void *, float);

int32_t sk_colorspace_transfer_fn_invert (void *, void *);

void sk_colorspace_transfer_fn_named_2dot2 (void *);

void sk_colorspace_transfer_fn_named_hlg (void *);

void sk_colorspace_transfer_fn_named_linear (void *);

void sk_colorspace_transfer_fn_named_pq (void *);

void sk_colorspace_transfer_fn_named_rec2020 (void *);

void sk_colorspace_transfer_fn_named_srgb (void *);

void sk_colorspace_unref (void *);

void sk_colorspace_xyz_concat (void *, void *, void *);

int32_t sk_colorspace_xyz_invert (void *, void *);

void sk_colorspace_xyz_named_adobe_rgb (void *);

void sk_colorspace_xyz_named_display_p3 (void *);

void sk_colorspace_xyz_named_rec2020 (void *);

void sk_colorspace_xyz_named_srgb (void *);

void sk_colorspace_xyz_named_xyz (void *);

int32_t sk_colortable_count (void *);

void * sk_colortable_new (void *, int32_t);

void sk_colortable_read_colors (void *, void *);

void sk_colortable_unref (void *);

int32_t sk_colortype_get_default_8888 ();

void * sk_compatpaint_clone (void *);

void sk_compatpaint_delete (void *);

void * sk_compatpaint_get_font (void *);

int32_t sk_compatpaint_get_text_align (void *);

int32_t sk_compatpaint_get_text_encoding (void *);

void * sk_compatpaint_make_font (void *);

void * sk_compatpaint_new ();

void * sk_compatpaint_new_with_font (void *);

void sk_compatpaint_reset (void *);

void sk_compatpaint_set_text_align (void *, int32_t);

void sk_compatpaint_set_text_encoding (void *, int32_t);

void * sk_data_get_bytes (void *);

void * sk_data_get_data (void *);

void * sk_data_get_size (void *);

void * sk_data_new_empty ();

void * sk_data_new_from_file (void *);

void * sk_data_new_from_stream (void *, void *);

void * sk_data_new_subset (void *, void *, void *);

void * sk_data_new_uninitialized (void *);

void * sk_data_new_with_copy (void *, void *);

void * sk_data_new_with_proc (void *, void *, void *, void *);

void sk_data_ref (void *);

void sk_data_unref (void *);

void sk_document_abort (void *);

void * sk_document_begin_page (void *, float, float, void *);

void sk_document_close (void *);

void * sk_document_create_pdf_from_stream (void *);

void * sk_document_create_pdf_from_stream_with_metadata (void *, void *);

void * sk_document_create_xps_from_stream (void *, float);

void sk_document_end_page (void *);

void sk_document_unref (void *);

void sk_drawable_draw (void *, void *, void *);

void sk_drawable_get_bounds (void *, void *);

uint32_t sk_drawable_get_generation_id (void *);

void * sk_drawable_new_picture_snapshot (void *);

void sk_drawable_notify_drawing_changed (void *);

void sk_drawable_unref (void *);

void sk_dynamicmemorywstream_copy_to (void *, void *);

void sk_dynamicmemorywstream_destroy (void *);

void * sk_dynamicmemorywstream_detach_as_data (void *);

void * sk_dynamicmemorywstream_detach_as_stream (void *);

void * sk_dynamicmemorywstream_new ();

int32_t sk_dynamicmemorywstream_write_to_stream (void *, void *);

void sk_filestream_destroy (void *);

int32_t sk_filestream_is_valid (void *);

void * sk_filestream_new (void *);

void sk_filewstream_destroy (void *);

int32_t sk_filewstream_is_valid (void *);

void * sk_filewstream_new (void *);

void * sk_font_break_text (void *, void *, void *, int32_t, float, void *, void *);

void sk_font_delete (void *);

int32_t sk_font_get_edging (void *);

int32_t sk_font_get_hinting (void *);

float sk_font_get_metrics (void *, void *);

int32_t sk_font_get_path (void *, uint32_t, void *);

void sk_font_get_paths (void *, void *, int32_t, void *, void *);

void sk_font_get_pos (void *, void *, int32_t, void *, void *);

float sk_font_get_scale_x (void *);

float sk_font_get_size (void *);

float sk_font_get_skew_x (void *);

void * sk_font_get_typeface (void *);

void sk_font_get_widths_bounds (void *, void *, int32_t, void *, void *, void *);

void sk_font_get_xpos (void *, void *, int32_t, void *, float);

int32_t sk_font_is_baseline_snap (void *);

int32_t sk_font_is_embedded_bitmaps (void *);

int32_t sk_font_is_embolden (void *);

int32_t sk_font_is_force_auto_hinting (void *);

int32_t sk_font_is_linear_metrics (void *);

int32_t sk_font_is_subpixel (void *);

float sk_font_measure_text (void *, void *, void *, int32_t, void *, void *);

void sk_font_measure_text_no_return (void *, void *, void *, int32_t, void *, void *, void *);

void * sk_font_new ();

void * sk_font_new_with_values (void *, float, float, float);

void sk_font_set_baseline_snap (void *, int32_t);

void sk_font_set_edging (void *, int32_t);

void sk_font_set_embedded_bitmaps (void *, int32_t);

void sk_font_set_embolden (void *, int32_t);

void sk_font_set_force_auto_hinting (void *, int32_t);

void sk_font_set_hinting (void *, int32_t);

void sk_font_set_linear_metrics (void *, int32_t);

void sk_font_set_scale_x (void *, float);

void sk_font_set_size (void *, float);

void sk_font_set_skew_x (void *, float);

void sk_font_set_subpixel (void *, int32_t);

void sk_font_set_typeface (void *, void *);

int32_t sk_font_text_to_glyphs (void *, void *, void *, int32_t, void *, int32_t);

uint32_t sk_font_unichar_to_glyph (void *, int32_t);

void sk_font_unichars_to_glyphs (void *, void *, int32_t, void *);

int32_t sk_fontmgr_count_families (void *);

void * sk_fontmgr_create_default ();

void * sk_fontmgr_create_from_data (void *, void *, int32_t);

void * sk_fontmgr_create_from_file (void *, void *, int32_t);

void * sk_fontmgr_create_from_stream (void *, void *, int32_t);

void * sk_fontmgr_create_styleset (void *, int32_t);

void sk_fontmgr_get_family_name (void *, int32_t, void *);

void * sk_fontmgr_match_face_style (void *, void *, void *);

void * sk_fontmgr_match_family (void *, void *);

void * sk_fontmgr_match_family_style (void *, void *, void *);

void * sk_fontmgr_match_family_style_character (void *, void *, void *, void *, int32_t, int32_t);

void * sk_fontmgr_ref_default ();

void sk_fontmgr_unref (void *);

void sk_fontstyle_delete (void *);

int32_t sk_fontstyle_get_slant (void *);

int32_t sk_fontstyle_get_weight (void *);

int32_t sk_fontstyle_get_width (void *);

void * sk_fontstyle_new (int32_t, int32_t, int32_t);

void * sk_fontstyleset_create_empty ();

void * sk_fontstyleset_create_typeface (void *, int32_t);

int32_t sk_fontstyleset_get_count (void *);

void sk_fontstyleset_get_style (void *, int32_t, void *, void *);

void * sk_fontstyleset_match_style (void *, void *);

void sk_fontstyleset_unref (void *);

void sk_graphics_dump_memory_statistics (void *);

int32_t sk_graphics_get_font_cache_count_limit ();

int32_t sk_graphics_get_font_cache_count_used ();

void * sk_graphics_get_font_cache_limit ();

int32_t sk_graphics_get_font_cache_point_size_limit ();

void * sk_graphics_get_font_cache_used ();

void * sk_graphics_get_resource_cache_single_allocation_byte_limit ();

void * sk_graphics_get_resource_cache_total_byte_limit ();

void * sk_graphics_get_resource_cache_total_bytes_used ();

void sk_graphics_init ();

void sk_graphics_purge_all_caches ();

void sk_graphics_purge_font_cache ();

void sk_graphics_purge_resource_cache ();

int32_t sk_graphics_set_font_cache_count_limit (int32_t);

void * sk_graphics_set_font_cache_limit (void *);

int32_t sk_graphics_set_font_cache_point_size_limit (int32_t);

void * sk_graphics_set_resource_cache_single_allocation_byte_limit (void *);

void * sk_graphics_set_resource_cache_total_byte_limit (void *);

void * sk_image_encode (void *);

void * sk_image_encode_specific (void *, int32_t, int32_t);

int32_t sk_image_get_alpha_type (void *);

int32_t sk_image_get_color_type (void *);

void * sk_image_get_colorspace (void *);

int32_t sk_image_get_height (void *);

uint32_t sk_image_get_unique_id (void *);

int32_t sk_image_get_width (void *);

int32_t sk_image_is_alpha_only (void *);

int32_t sk_image_is_lazy_generated (void *);

int32_t sk_image_is_texture_backed (void *);

int32_t sk_image_is_valid (void *, void *);

void * sk_image_make_non_texture_image (void *);

void * sk_image_make_raster_image (void *);

void * sk_image_make_shader (void *, int32_t, int32_t, void *);

void * sk_image_make_subset (void *, void *);

void * sk_image_make_texture_image (void *, void *, int32_t);

void * sk_image_make_with_filter (void *, void *, void *, void *, void *, void *, void *);

void * sk_image_make_with_filter_legacy (void *, void *, void *, void *, void *, void *);

void * sk_image_new_from_adopted_texture (void *, void *, int32_t, int32_t, int32_t, void *);

void * sk_image_new_from_bitmap (void *);

void * sk_image_new_from_encoded (void *);

void * sk_image_new_from_picture (void *, void *, void *, void *);

void * sk_image_new_from_texture (void *, void *, int32_t, int32_t, int32_t, void *, void *, void *);

void * sk_image_new_raster (void *, void *, void *);

void * sk_image_new_raster_copy (void *, void *, void *);

void * sk_image_new_raster_copy_with_pixmap (void *);

void * sk_image_new_raster_data (void *, void *, void *);

int32_t sk_image_peek_pixels (void *, void *);

int32_t sk_image_read_pixels (void *, void *, void *, void *, int32_t, int32_t, int32_t);

int32_t sk_image_read_pixels_into_pixmap (void *, void *, int32_t, int32_t, int32_t);

void sk_image_ref (void *);

void * sk_image_ref_encoded (void *);

int32_t sk_image_scale_pixels (void *, void *, int32_t, int32_t);

void sk_image_unref (void *);

void sk_imagefilter_croprect_destructor (void *);

uint32_t sk_imagefilter_croprect_get_flags (void *);

void sk_imagefilter_croprect_get_rect (void *, void *);

void * sk_imagefilter_croprect_new ();

void * sk_imagefilter_croprect_new_with_rect (void *, uint32_t);

void * sk_imagefilter_new_alpha_threshold (void *, float, float, void *);

void * sk_imagefilter_new_arithmetic (float, float, float, float, int32_t, void *, void *, void *);

void * sk_imagefilter_new_blur (float, float, int32_t, void *, void *);

void * sk_imagefilter_new_color_filter (void *, void *, void *);

void * sk_imagefilter_new_compose (void *, void *);

void * sk_imagefilter_new_dilate (float, float, void *, void *);

void * sk_imagefilter_new_displacement_map_effect (int32_t, int32_t, float, void *, void *, void *);

void * sk_imagefilter_new_distant_lit_diffuse (void *, uint32_t, float, float, void *, void *);

void * sk_imagefilter_new_distant_lit_specular (void *, uint32_t, float, float, float, void *, void *);

void * sk_imagefilter_new_drop_shadow (float, float, float, float, uint32_t, void *, void *);

void * sk_imagefilter_new_drop_shadow_only (float, float, float, float, uint32_t, void *, void *);

void * sk_imagefilter_new_erode (float, float, void *, void *);

void * sk_imagefilter_new_image_source (void *, void *, void *, int32_t);

void * sk_imagefilter_new_image_source_default (void *);

void * sk_imagefilter_new_magnifier (void *, float, void *, void *);

void * sk_imagefilter_new_matrix (void *, int32_t, void *);

void * sk_imagefilter_new_matrix_convolution (void *, void *, float, float, void *, int32_t, int32_t, void *, void *);

void * sk_imagefilter_new_merge (void *, int32_t, void *);

void * sk_imagefilter_new_offset (float, float, void *, void *);

void * sk_imagefilter_new_paint (void *, void *);

void * sk_imagefilter_new_picture (void *);

void * sk_imagefilter_new_picture_with_croprect (void *, void *);

void * sk_imagefilter_new_point_lit_diffuse (void *, uint32_t, float, float, void *, void *);

void * sk_imagefilter_new_point_lit_specular (void *, uint32_t, float, float, float, void *, void *);

void * sk_imagefilter_new_spot_lit_diffuse (void *, void *, float, float, uint32_t, float, float, void *, void *);

void * sk_imagefilter_new_spot_lit_specular (void *, void *, float, float, uint32_t, float, float, float, void *, void *);

void * sk_imagefilter_new_tile (void *, void *, void *);

void * sk_imagefilter_new_xfermode (int32_t, void *, void *, void *);

void sk_imagefilter_unref (void *);

int32_t sk_jpegencoder_encode (void *, void *, void *);

void * sk_manageddrawable_new (void *);

void sk_manageddrawable_set_procs (void *);

void sk_manageddrawable_unref (void *);

void sk_managedstream_destroy (void *);

void * sk_managedstream_new (void *);

void sk_managedstream_set_procs (void *);

void sk_managedtracememorydump_delete (void *);

void * sk_managedtracememorydump_new (int32_t, int32_t, void *);

void sk_managedtracememorydump_set_procs (void *);

void sk_managedwstream_destroy (void *);

void * sk_managedwstream_new (void *);

void sk_managedwstream_set_procs (void *);

void * sk_mask_alloc_image (void *);

void * sk_mask_compute_image_size (void *);

void * sk_mask_compute_total_image_size (void *);

void sk_mask_free_image (void *);

void * sk_mask_get_addr (void *, int32_t, int32_t);

void * sk_mask_get_addr_1 (void *, int32_t, int32_t);

void * sk_mask_get_addr_32 (void *, int32_t, int32_t);

void * sk_mask_get_addr_8 (void *, int32_t, int32_t);

void * sk_mask_get_addr_lcd_16 (void *, int32_t, int32_t);

int32_t sk_mask_is_empty (void *);

void * sk_maskfilter_new_blur (int32_t, float);

void * sk_maskfilter_new_blur_with_flags (int32_t, float, int32_t);

void * sk_maskfilter_new_clip (uint32_t, uint32_t);

void * sk_maskfilter_new_gamma (float);

void * sk_maskfilter_new_shader (void *);

void * sk_maskfilter_new_table (void *);

void sk_maskfilter_ref (void *);

void sk_maskfilter_unref (void *);

void sk_matrix_concat (void *, void *, void *);

void sk_matrix_map_points (void *, void *, void *, int32_t);

float sk_matrix_map_radius (void *, float);

void sk_matrix_map_rect (void *, void *, void *);

void sk_matrix_map_vector (void *, float, float, void *);

void sk_matrix_map_vectors (void *, void *, void *, int32_t);

void sk_matrix_map_xy (void *, float, float, void *);

void sk_matrix_post_concat (void *, void *);

void sk_matrix_pre_concat (void *, void *);

int32_t sk_matrix_try_invert (void *, void *);

void sk_matrix44_as_col_major (void *, void *);

void sk_matrix44_as_row_major (void *, void *);

void sk_matrix44_destroy (void *);

double sk_matrix44_determinant (void *);

int32_t sk_matrix44_equals (void *, void *);

float sk_matrix44_get (void *, int32_t, int32_t);

int32_t sk_matrix44_get_type (void *);

int32_t sk_matrix44_invert (void *, void *);

void sk_matrix44_map_scalars (void *, void *, void *);

void sk_matrix44_map2 (void *, void *, int32_t, void *);

void * sk_matrix44_new ();

void * sk_matrix44_new_concat (void *, void *);

void * sk_matrix44_new_copy (void *);

void * sk_matrix44_new_identity ();

void * sk_matrix44_new_matrix (void *);

void sk_matrix44_post_concat (void *, void *);

void sk_matrix44_post_scale (void *, float, float, float);

void sk_matrix44_post_translate (void *, float, float, float);

void sk_matrix44_pre_concat (void *, void *);

void sk_matrix44_pre_scale (void *, float, float, float);

void sk_matrix44_pre_translate (void *, float, float, float);

int32_t sk_matrix44_preserves_2d_axis_alignment (void *, float);

void sk_matrix44_set (void *, int32_t, int32_t, float);

void sk_matrix44_set_3x3_row_major (void *, void *);

void sk_matrix44_set_col_major (void *, void *);

void sk_matrix44_set_concat (void *, void *, void *);

void sk_matrix44_set_identity (void *);

void sk_matrix44_set_rotate_about_degrees (void *, float, float, float, float);

void sk_matrix44_set_rotate_about_radians (void *, float, float, float, float);

void sk_matrix44_set_rotate_about_radians_unit (void *, float, float, float, float);

void sk_matrix44_set_row_major (void *, void *);

void sk_matrix44_set_scale (void *, float, float, float);

void sk_matrix44_set_translate (void *, float, float, float);

void sk_matrix44_to_matrix (void *, void *);

void sk_matrix44_transpose (void *);

void sk_memorystream_destroy (void *);

void * sk_memorystream_new ();

void * sk_memorystream_new_with_data (void *, void *, int32_t);

void * sk_memorystream_new_with_length (void *);

void * sk_memorystream_new_with_skdata (void *);

void sk_memorystream_set_memory (void *, void *, void *, int32_t);

void sk_nodraw_canvas_destroy (void *);

void * sk_nodraw_canvas_new (int32_t, int32_t);

int32_t sk_nvrefcnt_get_ref_count (void *);

void sk_nvrefcnt_safe_ref (void *);

void sk_nvrefcnt_safe_unref (void *);

int32_t sk_nvrefcnt_unique (void *);

void sk_nway_canvas_add_canvas (void *, void *);

void sk_nway_canvas_destroy (void *);

void * sk_nway_canvas_new (int32_t, int32_t);

void sk_nway_canvas_remove_all (void *);

void sk_nway_canvas_remove_canvas (void *, void *);

void sk_opbuilder_add (void *, void *, int32_t);

void sk_opbuilder_destroy (void *);

void * sk_opbuilder_new ();

int32_t sk_opbuilder_resolve (void *, void *);

void sk_overdraw_canvas_destroy (void *);

void * sk_overdraw_canvas_new (void *);

void * sk_paint_clone (void *);

void sk_paint_delete (void *);

int32_t sk_paint_get_blendmode (void *);

uint32_t sk_paint_get_color (void *);

void sk_paint_get_color4f (void *, void *);

void * sk_paint_get_colorfilter (void *);

int32_t sk_paint_get_fill_path (void *, void *, void *, void *, float);

int32_t sk_paint_get_filter_quality (void *);

void * sk_paint_get_imagefilter (void *);

void * sk_paint_get_maskfilter (void *);

void * sk_paint_get_path_effect (void *);

void * sk_paint_get_shader (void *);

int32_t sk_paint_get_stroke_cap (void *);

int32_t sk_paint_get_stroke_join (void *);

float sk_paint_get_stroke_miter (void *);

float sk_paint_get_stroke_width (void *);

int32_t sk_paint_get_style (void *);

int32_t sk_paint_is_antialias (void *);

int32_t sk_paint_is_dither (void *);

void * sk_paint_new ();

void sk_paint_reset (void *);

void sk_paint_set_antialias (void *, int32_t);

void sk_paint_set_blendmode (void *, int32_t);

void sk_paint_set_color (void *, uint32_t);

void sk_paint_set_color4f (void *, void *, void *);

void sk_paint_set_colorfilter (void *, void *);

void sk_paint_set_dither (void *, int32_t);

void sk_paint_set_filter_quality (void *, int32_t);

void sk_paint_set_imagefilter (void *, void *);

void sk_paint_set_maskfilter (void *, void *);

void sk_paint_set_path_effect (void *, void *);

void sk_paint_set_shader (void *, void *);

void sk_paint_set_stroke_cap (void *, int32_t);

void sk_paint_set_stroke_join (void *, int32_t);

void sk_paint_set_stroke_miter (void *, float);

void sk_paint_set_stroke_width (void *, float);

void sk_paint_set_style (void *, int32_t);

void sk_path_add_arc (void *, void *, float, float);

void sk_path_add_circle (void *, float, float, float, int32_t);

void sk_path_add_oval (void *, void *, int32_t);

void sk_path_add_path (void *, void *, int32_t);

void sk_path_add_path_matrix (void *, void *, void *, int32_t);

void sk_path_add_path_offset (void *, void *, float, float, int32_t);

void sk_path_add_path_reverse (void *, void *);

void sk_path_add_poly (void *, void *, int32_t, int32_t);

void sk_path_add_rect (void *, void *, int32_t);

void sk_path_add_rect_start (void *, void *, int32_t, uint32_t);

void sk_path_add_rounded_rect (void *, void *, float, float, int32_t);

void sk_path_add_rrect (void *, void *, int32_t);

void sk_path_add_rrect_start (void *, void *, int32_t, uint32_t);

void sk_path_arc_to (void *, float, float, float, int32_t, int32_t, float, float);

void sk_path_arc_to_with_oval (void *, void *, float, float, int32_t);

void sk_path_arc_to_with_points (void *, float, float, float, float, float);

void * sk_path_clone (void *);

void sk_path_close (void *);

void sk_path_compute_tight_bounds (void *, void *);

void sk_path_conic_to (void *, float, float, float, float, float);

int32_t sk_path_contains (void *, float, float);

int32_t sk_path_convert_conic_to_quads (void *, void *, void *, float, void *, int32_t);

int32_t sk_path_count_points (void *);

int32_t sk_path_count_verbs (void *);

void * sk_path_create_iter (void *, int32_t);

void * sk_path_create_rawiter (void *);

void sk_path_cubic_to (void *, float, float, float, float, float, float);

void sk_path_delete (void *);

void * sk_path_effect_create_1d_path (void *, float, float, int32_t);

void * sk_path_effect_create_2d_line (float, void *);

void * sk_path_effect_create_2d_path (void *, void *);

void * sk_path_effect_create_compose (void *, void *);

void * sk_path_effect_create_corner (float);

void * sk_path_effect_create_dash (void *, int32_t, float);

void * sk_path_effect_create_discrete (float, float, uint32_t);

void * sk_path_effect_create_sum (void *, void *);

void * sk_path_effect_create_trim (float, float, int32_t);

void sk_path_effect_unref (void *);

void sk_path_get_bounds (void *, void *);

int32_t sk_path_get_filltype (void *);

int32_t sk_path_get_last_point (void *, void *);

void sk_path_get_point (void *, int32_t, void *);

int32_t sk_path_get_points (void *, void *, int32_t);

uint32_t sk_path_get_segment_masks (void *);

int32_t sk_path_is_convex (void *);

int32_t sk_path_is_line (void *, void *);

int32_t sk_path_is_oval (void *, void *);

int32_t sk_path_is_rect (void *, void *, void *, void *);

int32_t sk_path_is_rrect (void *, void *);

float sk_path_iter_conic_weight (void *);

void sk_path_iter_destroy (void *);

int32_t sk_path_iter_is_close_line (void *);

int32_t sk_path_iter_is_closed_contour (void *);

int32_t sk_path_iter_next (void *, void *);

void sk_path_line_to (void *, float, float);

void sk_path_move_to (void *, float, float);

void * sk_path_new ();

int32_t sk_path_parse_svg_string (void *, void *);

void sk_path_quad_to (void *, float, float, float, float);

void sk_path_rarc_to (void *, float, float, float, int32_t, int32_t, float, float);

float sk_path_rawiter_conic_weight (void *);

void sk_path_rawiter_destroy (void *);

int32_t sk_path_rawiter_next (void *, void *);

int32_t sk_path_rawiter_peek (void *);

void sk_path_rconic_to (void *, float, float, float, float, float);

void sk_path_rcubic_to (void *, float, float, float, float, float, float);

void sk_path_reset (void *);

void sk_path_rewind (void *);

void sk_path_rline_to (void *, float, float);

void sk_path_rmove_to (void *, float, float);

void sk_path_rquad_to (void *, float, float, float, float);

void sk_path_set_filltype (void *, int32_t);

void sk_path_to_svg_string (void *, void *);

void sk_path_transform (void *, void *);

void sk_path_transform_to_dest (void *, void *, void *);

void sk_pathmeasure_destroy (void *);

float sk_pathmeasure_get_length (void *);

int32_t sk_pathmeasure_get_matrix (void *, float, void *, int32_t);

int32_t sk_pathmeasure_get_pos_tan (void *, float, void *, void *);

int32_t sk_pathmeasure_get_segment (void *, float, float, void *, int32_t);

int32_t sk_pathmeasure_is_closed (void *);

void * sk_pathmeasure_new ();

void * sk_pathmeasure_new_with_path (void *, int32_t, float);

int32_t sk_pathmeasure_next_contour (void *);

void sk_pathmeasure_set_path (void *, void *, int32_t);

int32_t sk_pathop_as_winding (void *, void *);

int32_t sk_pathop_op (void *, void *, int32_t, void *);

int32_t sk_pathop_simplify (void *, void *);

int32_t sk_pathop_tight_bounds (void *, void *);

void * sk_picture_deserialize_from_data (void *);

void * sk_picture_deserialize_from_memory (void *, void *);

void * sk_picture_deserialize_from_stream (void *);

void sk_picture_get_cull_rect (void *, void *);

void * sk_picture_get_recording_canvas (void *);

uint32_t sk_picture_get_unique_id (void *);

void * sk_picture_make_shader (void *, int32_t, int32_t, void *, void *);

void * sk_picture_recorder_begin_recording (void *, void *);

void sk_picture_recorder_delete (void *);

void * sk_picture_recorder_end_recording (void *);

void * sk_picture_recorder_end_recording_as_drawable (void *);

void * sk_picture_recorder_new ();

void sk_picture_ref (void *);

void * sk_picture_serialize_to_data (void *);

void sk_picture_serialize_to_stream (void *, void *);

void sk_picture_unref (void *);

void sk_pixmap_destructor (void *);

int32_t sk_pixmap_encode_image (void *, void *, int32_t, int32_t);

int32_t sk_pixmap_erase_color (void *, uint32_t, void *);

int32_t sk_pixmap_erase_color4f (void *, void *, void *, void *);

int32_t sk_pixmap_extract_subset (void *, void *, void *);

void sk_pixmap_get_info (void *, void *);

uint32_t sk_pixmap_get_pixel_color (void *, int32_t, int32_t);

void * sk_pixmap_get_pixels (void *);

void * sk_pixmap_get_pixels_with_xy (void *, int32_t, int32_t);

void * sk_pixmap_get_row_bytes (void *);

void * sk_pixmap_get_writable_addr (void *);

void * sk_pixmap_new ();

void * sk_pixmap_new_with_params (void *, void *, void *);

int32_t sk_pixmap_read_pixels (void *, void *, void *, void *, int32_t, int32_t);

void sk_pixmap_reset (void *);

void sk_pixmap_reset_with_params (void *, void *, void *, void *);

int32_t sk_pixmap_scale_pixels (void *, void *, int32_t);

int32_t sk_pngencoder_encode (void *, void *, void *);

int32_t sk_refcnt_get_ref_count (void *);

void sk_refcnt_safe_ref (void *);

void sk_refcnt_safe_unref (void *);

int32_t sk_refcnt_unique (void *);

void sk_region_cliperator_delete (void *);

int32_t sk_region_cliperator_done (void *);

void * sk_region_cliperator_new (void *, void *);

void sk_region_cliperator_next (void *);

void sk_region_cliperator_rect (void *, void *);

int32_t sk_region_contains (void *, void *);

int32_t sk_region_contains_point (void *, int32_t, int32_t);

int32_t sk_region_contains_rect (void *, void *);

void sk_region_delete (void *);

int32_t sk_region_get_boundary_path (void *, void *);

void sk_region_get_bounds (void *, void *);

int32_t sk_region_intersects (void *, void *);

int32_t sk_region_intersects_rect (void *, void *);

int32_t sk_region_is_complex (void *);

int32_t sk_region_is_empty (void *);

int32_t sk_region_is_rect (void *);

void sk_region_iterator_delete (void *);

int32_t sk_region_iterator_done (void *);

void * sk_region_iterator_new (void *);

void sk_region_iterator_next (void *);

void sk_region_iterator_rect (void *, void *);

int32_t sk_region_iterator_rewind (void *);

void * sk_region_new ();

int32_t sk_region_op (void *, void *, int32_t);

int32_t sk_region_op_rect (void *, void *, int32_t);

int32_t sk_region_quick_contains (void *, void *);

int32_t sk_region_quick_reject (void *, void *);

int32_t sk_region_quick_reject_rect (void *, void *);

int32_t sk_region_set_empty (void *);

int32_t sk_region_set_path (void *, void *, void *);

int32_t sk_region_set_rect (void *, void *);

int32_t sk_region_set_rects (void *, void *, int32_t);

int32_t sk_region_set_region (void *, void *);

void sk_region_spanerator_delete (void *);

void * sk_region_spanerator_new (void *, int32_t, int32_t, int32_t);

int32_t sk_region_spanerator_next (void *, void *, void *);

void sk_region_translate (void *, int32_t, int32_t);

int32_t sk_rrect_contains (void *, void *);

void sk_rrect_delete (void *);

float sk_rrect_get_height (void *);

void sk_rrect_get_radii (void *, int32_t, void *);

void sk_rrect_get_rect (void *, void *);

int32_t sk_rrect_get_type (void *);

float sk_rrect_get_width (void *);

void sk_rrect_inset (void *, float, float);

int32_t sk_rrect_is_valid (void *);

void * sk_rrect_new ();

void * sk_rrect_new_copy (void *);

void sk_rrect_offset (void *, float, float);

void sk_rrect_outset (void *, float, float);

void sk_rrect_set_empty (void *);

void sk_rrect_set_nine_patch (void *, void *, float, float, float, float);

void sk_rrect_set_oval (void *, void *);

void sk_rrect_set_rect (void *, void *);

void sk_rrect_set_rect_radii (void *, void *, void *);

void sk_rrect_set_rect_xy (void *, void *, float, float);

int32_t sk_rrect_transform (void *, void *, void *);

void sk_runtimeeffect_get_child_name (void *, int32_t, void *);

void * sk_runtimeeffect_get_children_count (void *);

void * sk_runtimeeffect_get_uniform_from_index (void *, int32_t);

void * sk_runtimeeffect_get_uniform_from_name (void *, void *, void *);

void sk_runtimeeffect_get_uniform_name (void *, int32_t, void *);

void * sk_runtimeeffect_get_uniform_size (void *);

void * sk_runtimeeffect_get_uniforms_count (void *);

void * sk_runtimeeffect_make (void *, void *);

void * sk_runtimeeffect_make_color_filter (void *, void *, void *, void *);

void * sk_runtimeeffect_make_shader (void *, void *, void *, void *, void *, int32_t);

void * sk_runtimeeffect_uniform_get_offset (void *);

void * sk_runtimeeffect_uniform_get_size_in_bytes (void *);

void sk_runtimeeffect_unref (void *);

void * sk_shader_new_blend (int32_t, void *, void *);

void * sk_shader_new_color (uint32_t);

void * sk_shader_new_color4f (void *, void *);

void * sk_shader_new_empty ();

void * sk_shader_new_lerp (float, void *, void *);

void * sk_shader_new_linear_gradient (void *, void *, void *, int32_t, int32_t, void *);

void * sk_shader_new_linear_gradient_color4f (void *, void *, void *, void *, int32_t, int32_t, void *);

void * sk_shader_new_perlin_noise_fractal_noise (float, float, int32_t, float, void *);

void * sk_shader_new_perlin_noise_improved_noise (float, float, int32_t, float);

void * sk_shader_new_perlin_noise_turbulence (float, float, int32_t, float, void *);

void * sk_shader_new_radial_gradient (void *, float, void *, void *, int32_t, int32_t, void *);

void * sk_shader_new_radial_gradient_color4f (void *, float, void *, void *, void *, int32_t, int32_t, void *);

void * sk_shader_new_sweep_gradient (void *, void *, void *, int32_t, int32_t, float, float, void *);

void * sk_shader_new_sweep_gradient_color4f (void *, void *, void *, void *, int32_t, int32_t, float, float, void *);

void * sk_shader_new_two_point_conical_gradient (void *, float, void *, float, void *, void *, int32_t, int32_t, void *);

void * sk_shader_new_two_point_conical_gradient_color4f (void *, float, void *, float, void *, void *, void *, int32_t, int32_t, void *);

void sk_shader_ref (void *);

void sk_shader_unref (void *);

void * sk_shader_with_color_filter (void *, void *);

void * sk_shader_with_local_matrix (void *, void *);

void sk_stream_asset_destroy (void *);

void sk_stream_destroy (void *);

void * sk_stream_duplicate (void *);

void * sk_stream_fork (void *);

void * sk_stream_get_length (void *);

void * sk_stream_get_memory_base (void *);

void * sk_stream_get_position (void *);

int32_t sk_stream_has_length (void *);

int32_t sk_stream_has_position (void *);

int32_t sk_stream_is_at_end (void *);

int32_t sk_stream_move (void *, int32_t);

void * sk_stream_peek (void *, void *, void *);

void * sk_stream_read (void *, void *, void *);

int32_t sk_stream_read_bool (void *, void *);

int32_t sk_stream_read_s16 (void *, void *);

int32_t sk_stream_read_s32 (void *, void *);

int32_t sk_stream_read_s8 (void *, void *);

int32_t sk_stream_read_u16 (void *, void *);

int32_t sk_stream_read_u32 (void *, void *);

int32_t sk_stream_read_u8 (void *, void *);

int32_t sk_stream_rewind (void *);

int32_t sk_stream_seek (void *, void *);

void * sk_stream_skip (void *, void *);

void sk_string_destructor (void *);

void * sk_string_get_c_str (void *);

void * sk_string_get_size (void *);

void * sk_string_new_empty ();

void * sk_string_new_with_copy (void *, void *);

void sk_surface_draw (void *, void *, float, float, void *);

void sk_surface_flush (void *);

void sk_surface_flush_and_submit (void *, int32_t);

void * sk_surface_get_canvas (void *);

void * sk_surface_get_props (void *);

void * sk_surface_get_recording_context (void *);

void * sk_surface_new_backend_render_target (void *, void *, int32_t, int32_t, void *, void *);

void * sk_surface_new_backend_texture (void *, void *, int32_t, int32_t, int32_t, void *, void *);

void * sk_surface_new_image_snapshot (void *);

void * sk_surface_new_image_snapshot_with_crop (void *, void *);

void * sk_surface_new_metal_layer (void *, void *, int32_t, int32_t, int32_t, void *, void *, void *);

void * sk_surface_new_metal_view (void *, void *, int32_t, int32_t, int32_t, void *, void *);

void * sk_surface_new_null (int32_t, int32_t);

void * sk_surface_new_raster (void *, void *, void *);

void * sk_surface_new_raster_direct (void *, void *, void *, void *, void *, void *);

void * sk_surface_new_render_target (void *, int32_t, void *, int32_t, int32_t, void *, int32_t);

int32_t sk_surface_peek_pixels (void *, void *);

int32_t sk_surface_read_pixels (void *, void *, void *, void *, int32_t, int32_t);

void sk_surface_unref (void *);

void sk_surfaceprops_delete (void *);

uint32_t sk_surfaceprops_get_flags (void *);

int32_t sk_surfaceprops_get_pixel_geometry (void *);

void * sk_surfaceprops_new (uint32_t, int32_t);

void * sk_svgcanvas_create_with_stream (void *, void *);

void * sk_svgcanvas_create_with_writer (void *, void *);

void sk_swizzle_swap_rb (void *, void *, int32_t);

void sk_text_utils_get_path (void *, void *, int32_t, float, float, void *, void *);

void sk_text_utils_get_pos_path (void *, void *, int32_t, void *, void *, void *);

void sk_textblob_builder_alloc_run (void *, void *, int32_t, float, float, void *, void *);

void sk_textblob_builder_alloc_run_pos (void *, void *, int32_t, void *, void *);

void sk_textblob_builder_alloc_run_pos_h (void *, void *, int32_t, float, void *, void *);

void sk_textblob_builder_alloc_run_rsxform (void *, void *, int32_t, void *);

void sk_textblob_builder_alloc_run_text (void *, void *, int32_t, float, float, int32_t, void *, void *);

void sk_textblob_builder_alloc_run_text_pos (void *, void *, int32_t, int32_t, void *, void *);

void sk_textblob_builder_alloc_run_text_pos_h (void *, void *, int32_t, float, int32_t, void *, void *);

void sk_textblob_builder_delete (void *);

void * sk_textblob_builder_make (void *);

void * sk_textblob_builder_new ();

void sk_textblob_get_bounds (void *, void *);

int32_t sk_textblob_get_intercepts (void *, void *, void *, void *);

uint32_t sk_textblob_get_unique_id (void *);

void sk_textblob_ref (void *);

void sk_textblob_unref (void *);

void * sk_typeface_copy_table_data (void *, uint32_t);

int32_t sk_typeface_count_glyphs (void *);

int32_t sk_typeface_count_tables (void *);

void * sk_typeface_create_default ();

void * sk_typeface_create_from_data (void *, int32_t);

void * sk_typeface_create_from_file (void *, int32_t);

void * sk_typeface_create_from_name (void *, void *);

void * sk_typeface_create_from_stream (void *, int32_t);

void * sk_typeface_get_family_name (void *);

int32_t sk_typeface_get_font_slant (void *);

int32_t sk_typeface_get_font_weight (void *);

int32_t sk_typeface_get_font_width (void *);

void * sk_typeface_get_fontstyle (void *);

int32_t sk_typeface_get_kerning_pair_adjustments (void *, void *, int32_t, void *);

void * sk_typeface_get_table_data (void *, uint32_t, void *, void *, void *);

void * sk_typeface_get_table_size (void *, uint32_t);

int32_t sk_typeface_get_table_tags (void *, void *);

int32_t sk_typeface_get_units_per_em (void *);

int32_t sk_typeface_is_fixed_pitch (void *);

void * sk_typeface_open_stream (void *, void *);

void * sk_typeface_ref_default ();

uint32_t sk_typeface_unichar_to_glyph (void *, int32_t);

void sk_typeface_unichars_to_glyphs (void *, void *, int32_t, void *);

void sk_typeface_unref (void *);

int32_t sk_version_get_increment ();

int32_t sk_version_get_milestone ();

void * sk_version_get_string ();

void * sk_vertices_make_copy (int32_t, int32_t, void *, void *, void *, int32_t, void *);

void sk_vertices_ref (void *);

void sk_vertices_unref (void *);

int32_t sk_webpencoder_encode (void *, void *, void *);

void * sk_wstream_bytes_written (void *);

void sk_wstream_flush (void *);

int32_t sk_wstream_get_size_of_packed_uint (void *);

int32_t sk_wstream_newline (void *);

int32_t sk_wstream_write (void *, void *, void *);

int32_t sk_wstream_write_16 (void *, uint32_t);

int32_t sk_wstream_write_32 (void *, uint32_t);

int32_t sk_wstream_write_8 (void *, uint32_t);

int32_t sk_wstream_write_bigdec_as_text (void *, int64_t, int32_t);

int32_t sk_wstream_write_bool (void *, int32_t);

int32_t sk_wstream_write_dec_as_text (void *, int32_t);

int32_t sk_wstream_write_hex_as_text (void *, uint32_t, int32_t);

int32_t sk_wstream_write_packed_uint (void *, void *);

int32_t sk_wstream_write_scalar (void *, float);

int32_t sk_wstream_write_scalar_as_text (void *, float);

int32_t sk_wstream_write_stream (void *, void *, void *);

int32_t sk_wstream_write_text (void *, void *);

void sk_xmlstreamwriter_delete (void *);

void * sk_xmlstreamwriter_new (void *);

int32_t SystemNative_Access (void *, int32_t);

void * SystemNative_AlignedAlloc (void *, void *);

void SystemNative_AlignedFree (void *);

void * SystemNative_AlignedRealloc (void *, void *, void *);

void * SystemNative_Calloc (void *, void *);

int32_t SystemNative_CanGetHiddenFlag ();

int32_t SystemNative_ChDir (void *);

int32_t SystemNative_ChMod (void *, int32_t);

int32_t SystemNative_Close (void *);

int32_t SystemNative_CloseDir (void *);

int32_t SystemNative_ConvertErrorPalToPlatform (int32_t);

int32_t SystemNative_ConvertErrorPlatformToPal (int32_t);

int32_t SystemNative_CopyFile (void *, void *, int64_t);

void * SystemNative_Dup (void *);

int32_t SystemNative_FAllocate (void *, int64_t, int64_t);

int32_t SystemNative_FChflags (void *, uint32_t);

int32_t SystemNative_FChMod (void *, int32_t);

int32_t SystemNative_FcntlSetFD (void *, int32_t);

int32_t SystemNative_FLock (void *, int32_t);

void SystemNative_Free (void *);

void SystemNative_FreeEnviron (void *);

int32_t SystemNative_FStat (void *, void *);

int32_t SystemNative_FSync (void *);

int32_t SystemNative_FTruncate (void *, int64_t);

int32_t SystemNative_FUTimens (void *, void *);

int32_t SystemNative_GetAddressFamily (void *, int32_t, void *);

double SystemNative_GetCpuUtilization (void *);

int32_t SystemNative_GetCryptographicallySecureRandomBytes (void *, int32_t);

void * SystemNative_GetCwd (void *, int32_t);

void * SystemNative_GetDefaultSearchOrderPseudoHandle ();

void * SystemNative_GetEnv (void *);

void * SystemNative_GetEnviron ();

int32_t SystemNative_GetErrNo ();

uint32_t SystemNative_GetFileSystemType (void *);

int32_t SystemNative_GetIPv4Address (void *, int32_t, void *);

int32_t SystemNative_GetIPv6Address (void *, int32_t, void *, int32_t, void *);

void SystemNative_GetNonCryptographicallySecureRandomBytes (void *, int32_t);

int32_t SystemNative_GetPort (void *, int32_t, void *);

int32_t SystemNative_GetReadDirRBufferSize ();

int32_t SystemNative_GetSocketAddressSizes (void *, void *, void *, void *);

int64_t SystemNative_GetSystemTimeAsTicks ();

uint64_t SystemNative_GetTimestamp ();

void * SystemNative_GetTimeZoneData (void *, void *);

int32_t SystemNative_LChflags (void *, uint32_t);

int32_t SystemNative_LChflagsCanSetHiddenFlag ();

int32_t SystemNative_Link (void *, void *);

int32_t SystemNative_LockFileRegion (void *, int64_t, int64_t, int32_t);

void SystemNative_Log (void *, int32_t);

void SystemNative_LogError (void *, int32_t);

void SystemNative_LowLevelMonitor_Acquire (void *);

void * SystemNative_LowLevelMonitor_Create ();

void SystemNative_LowLevelMonitor_Destroy (void *);

void SystemNative_LowLevelMonitor_Release (void *);

void SystemNative_LowLevelMonitor_Signal_Release (void *);

int32_t SystemNative_LowLevelMonitor_TimedWait (void *, int32_t);

void SystemNative_LowLevelMonitor_Wait (void *);

int64_t SystemNative_LSeek (void *, int64_t, int32_t);

int32_t SystemNative_LStat (void *, void *);

int32_t SystemNative_MAdvise (void *, uint64_t, int32_t);

void * SystemNative_Malloc (void *);

int32_t SystemNative_MkDir (void *, int32_t);

void * SystemNative_MkdTemp (void *);

void * SystemNative_MksTemps (void *, int32_t);

void * SystemNative_MMap (void *, uint64_t, int32_t, int32_t, void *, int64_t);

int32_t SystemNative_MSync (void *, uint64_t, int32_t);

int32_t SystemNative_MUnmap (void *, uint64_t);

void * SystemNative_Open (void *, int32_t, int32_t);

void * SystemNative_OpenDir (void *);

int32_t SystemNative_PosixFAdvise (void *, int64_t, int64_t, int32_t);

int32_t SystemNative_PRead (void *, void *, int32_t, int64_t);

int64_t SystemNative_PReadV (void *, void *, int32_t, int64_t);

int32_t SystemNative_PWrite (void *, void *, int32_t, int64_t);

int64_t SystemNative_PWriteV (void *, void *, int32_t, int64_t);

int32_t SystemNative_Read (void *, void *, int32_t);

int32_t SystemNative_ReadDirR (void *, void *, int32_t, void *);

int32_t SystemNative_ReadLink (void *, void *, int32_t);

void * SystemNative_Realloc (void *, void *);

int32_t SystemNative_Rename (void *, void *);

int32_t SystemNative_RmDir (void *);

int32_t SystemNative_SchedGetCpu ();

int32_t SystemNative_SetAddressFamily (void *, int32_t, int32_t);

void SystemNative_SetErrNo (int32_t);

int32_t SystemNative_SetIPv4Address (void *, int32_t, uint32_t);

int32_t SystemNative_SetIPv6Address (void *, int32_t, void *, int32_t, uint32_t);

int32_t SystemNative_SetPort (void *, int32_t, uint32_t);

void * SystemNative_ShmOpen (void *, int32_t, int32_t);

int32_t SystemNative_ShmUnlink (void *);

int32_t SystemNative_Stat (void *, void *);

void * SystemNative_StrErrorR (int32_t, void *, int32_t);

int32_t SystemNative_SymLink (void *, void *);

int64_t SystemNative_SysConf (int32_t);

void SystemNative_SysLog (int32_t, void *, void *);

uint32_t SystemNative_TryGetUInt32OSThreadId ();

int32_t SystemNative_Unlink (void *);

int32_t SystemNative_UTimensat (void *, void *);

int32_t SystemNative_Write (void *, void *, int32_t);
static PinvokeImport libSkiaSharp_imports [] = {
    {"eglGetProcAddress", eglGetProcAddress}, // Avalonia.Browser
    {"gr_backendrendertarget_delete", gr_backendrendertarget_delete}, // SkiaSharp
    {"gr_backendrendertarget_get_backend", gr_backendrendertarget_get_backend}, // SkiaSharp
    {"gr_backendrendertarget_get_gl_framebufferinfo", gr_backendrendertarget_get_gl_framebufferinfo}, // SkiaSharp
    {"gr_backendrendertarget_get_height", gr_backendrendertarget_get_height}, // SkiaSharp
    {"gr_backendrendertarget_get_samples", gr_backendrendertarget_get_samples}, // SkiaSharp
    {"gr_backendrendertarget_get_stencils", gr_backendrendertarget_get_stencils}, // SkiaSharp
    {"gr_backendrendertarget_get_width", gr_backendrendertarget_get_width}, // SkiaSharp
    {"gr_backendrendertarget_is_valid", gr_backendrendertarget_is_valid}, // SkiaSharp
    {"gr_backendrendertarget_new_gl", gr_backendrendertarget_new_gl}, // SkiaSharp
    {"gr_backendrendertarget_new_metal", gr_backendrendertarget_new_metal}, // SkiaSharp
    {"gr_backendrendertarget_new_vulkan", gr_backendrendertarget_new_vulkan}, // SkiaSharp
    {"gr_backendtexture_delete", gr_backendtexture_delete}, // SkiaSharp
    {"gr_backendtexture_get_backend", gr_backendtexture_get_backend}, // SkiaSharp
    {"gr_backendtexture_get_gl_textureinfo", gr_backendtexture_get_gl_textureinfo}, // SkiaSharp
    {"gr_backendtexture_get_height", gr_backendtexture_get_height}, // SkiaSharp
    {"gr_backendtexture_get_width", gr_backendtexture_get_width}, // SkiaSharp
    {"gr_backendtexture_has_mipmaps", gr_backendtexture_has_mipmaps}, // SkiaSharp
    {"gr_backendtexture_is_valid", gr_backendtexture_is_valid}, // SkiaSharp
    {"gr_backendtexture_new_gl", gr_backendtexture_new_gl}, // SkiaSharp
    {"gr_backendtexture_new_metal", gr_backendtexture_new_metal}, // SkiaSharp
    {"gr_backendtexture_new_vulkan", gr_backendtexture_new_vulkan}, // SkiaSharp
    {"gr_direct_context_abandon_context", gr_direct_context_abandon_context}, // SkiaSharp
    {"gr_direct_context_dump_memory_statistics", gr_direct_context_dump_memory_statistics}, // SkiaSharp
    {"gr_direct_context_flush", gr_direct_context_flush}, // SkiaSharp
    {"gr_direct_context_flush_and_submit", gr_direct_context_flush_and_submit}, // SkiaSharp
    {"gr_direct_context_free_gpu_resources", gr_direct_context_free_gpu_resources}, // SkiaSharp
    {"gr_direct_context_get_resource_cache_limit", gr_direct_context_get_resource_cache_limit}, // SkiaSharp
    {"gr_direct_context_get_resource_cache_usage", gr_direct_context_get_resource_cache_usage}, // SkiaSharp
    {"gr_direct_context_is_abandoned", gr_direct_context_is_abandoned}, // SkiaSharp
    {"gr_direct_context_make_gl", gr_direct_context_make_gl}, // SkiaSharp
    {"gr_direct_context_make_gl_with_options", gr_direct_context_make_gl_with_options}, // SkiaSharp
    {"gr_direct_context_make_metal", gr_direct_context_make_metal}, // SkiaSharp
    {"gr_direct_context_make_metal_with_options", gr_direct_context_make_metal_with_options}, // SkiaSharp
    {"gr_direct_context_make_vulkan", gr_direct_context_make_vulkan}, // SkiaSharp
    {"gr_direct_context_make_vulkan_with_options", gr_direct_context_make_vulkan_with_options}, // SkiaSharp
    {"gr_direct_context_perform_deferred_cleanup", gr_direct_context_perform_deferred_cleanup}, // SkiaSharp
    {"gr_direct_context_purge_unlocked_resources", gr_direct_context_purge_unlocked_resources}, // SkiaSharp
    {"gr_direct_context_purge_unlocked_resources_bytes", gr_direct_context_purge_unlocked_resources_bytes}, // SkiaSharp
    {"gr_direct_context_release_resources_and_abandon_context", gr_direct_context_release_resources_and_abandon_context}, // SkiaSharp
    {"gr_direct_context_reset_context", gr_direct_context_reset_context}, // SkiaSharp
    {"gr_direct_context_set_resource_cache_limit", gr_direct_context_set_resource_cache_limit}, // SkiaSharp
    {"gr_direct_context_submit", gr_direct_context_submit}, // SkiaSharp
    {"gr_glinterface_assemble_gl_interface", gr_glinterface_assemble_gl_interface}, // SkiaSharp
    {"gr_glinterface_assemble_gles_interface", gr_glinterface_assemble_gles_interface}, // SkiaSharp
    {"gr_glinterface_assemble_interface", gr_glinterface_assemble_interface}, // SkiaSharp
    {"gr_glinterface_assemble_webgl_interface", gr_glinterface_assemble_webgl_interface}, // SkiaSharp
    {"gr_glinterface_create_native_interface", gr_glinterface_create_native_interface}, // SkiaSharp
    {"gr_glinterface_has_extension", gr_glinterface_has_extension}, // SkiaSharp
    {"gr_glinterface_unref", gr_glinterface_unref}, // SkiaSharp
    {"gr_glinterface_validate", gr_glinterface_validate}, // SkiaSharp
    {"gr_recording_context_get_backend", gr_recording_context_get_backend}, // SkiaSharp
    {"gr_recording_context_get_max_surface_sample_count_for_color_type", gr_recording_context_get_max_surface_sample_count_for_color_type}, // SkiaSharp
    {"gr_recording_context_unref", gr_recording_context_unref}, // SkiaSharp
    {"gr_vk_extensions_delete", gr_vk_extensions_delete}, // SkiaSharp
    {"gr_vk_extensions_has_extension", gr_vk_extensions_has_extension}, // SkiaSharp
    {"gr_vk_extensions_init", gr_vk_extensions_init}, // SkiaSharp
    {"gr_vk_extensions_new", gr_vk_extensions_new}, // SkiaSharp
    {"sk_3dview_apply_to_canvas", sk_3dview_apply_to_canvas}, // SkiaSharp
    {"sk_3dview_destroy", sk_3dview_destroy}, // SkiaSharp
    {"sk_3dview_dot_with_normal", sk_3dview_dot_with_normal}, // SkiaSharp
    {"sk_3dview_get_matrix", sk_3dview_get_matrix}, // SkiaSharp
    {"sk_3dview_new", sk_3dview_new}, // SkiaSharp
    {"sk_3dview_restore", sk_3dview_restore}, // SkiaSharp
    {"sk_3dview_rotate_x_degrees", sk_3dview_rotate_x_degrees}, // SkiaSharp
    {"sk_3dview_rotate_x_radians", sk_3dview_rotate_x_radians}, // SkiaSharp
    {"sk_3dview_rotate_y_degrees", sk_3dview_rotate_y_degrees}, // SkiaSharp
    {"sk_3dview_rotate_y_radians", sk_3dview_rotate_y_radians}, // SkiaSharp
    {"sk_3dview_rotate_z_degrees", sk_3dview_rotate_z_degrees}, // SkiaSharp
    {"sk_3dview_rotate_z_radians", sk_3dview_rotate_z_radians}, // SkiaSharp
    {"sk_3dview_save", sk_3dview_save}, // SkiaSharp
    {"sk_3dview_translate", sk_3dview_translate}, // SkiaSharp
    {"sk_bitmap_destructor", sk_bitmap_destructor}, // SkiaSharp
    {"sk_bitmap_erase", sk_bitmap_erase}, // SkiaSharp
    {"sk_bitmap_erase_rect", sk_bitmap_erase_rect}, // SkiaSharp
    {"sk_bitmap_extract_alpha", sk_bitmap_extract_alpha}, // SkiaSharp
    {"sk_bitmap_extract_subset", sk_bitmap_extract_subset}, // SkiaSharp
    {"sk_bitmap_get_addr", sk_bitmap_get_addr}, // SkiaSharp
    {"sk_bitmap_get_addr_16", sk_bitmap_get_addr_16}, // SkiaSharp
    {"sk_bitmap_get_addr_32", sk_bitmap_get_addr_32}, // SkiaSharp
    {"sk_bitmap_get_addr_8", sk_bitmap_get_addr_8}, // SkiaSharp
    {"sk_bitmap_get_byte_count", sk_bitmap_get_byte_count}, // SkiaSharp
    {"sk_bitmap_get_info", sk_bitmap_get_info}, // SkiaSharp
    {"sk_bitmap_get_pixel_color", sk_bitmap_get_pixel_color}, // SkiaSharp
    {"sk_bitmap_get_pixel_colors", sk_bitmap_get_pixel_colors}, // SkiaSharp
    {"sk_bitmap_get_pixels", sk_bitmap_get_pixels}, // SkiaSharp
    {"sk_bitmap_get_row_bytes", sk_bitmap_get_row_bytes}, // SkiaSharp
    {"sk_bitmap_install_mask_pixels", sk_bitmap_install_mask_pixels}, // SkiaSharp
    {"sk_bitmap_install_pixels", sk_bitmap_install_pixels}, // SkiaSharp
    {"sk_bitmap_install_pixels_with_pixmap", sk_bitmap_install_pixels_with_pixmap}, // SkiaSharp
    {"sk_bitmap_is_immutable", sk_bitmap_is_immutable}, // SkiaSharp
    {"sk_bitmap_is_null", sk_bitmap_is_null}, // SkiaSharp
    {"sk_bitmap_make_shader", sk_bitmap_make_shader}, // SkiaSharp
    {"sk_bitmap_new", sk_bitmap_new}, // SkiaSharp
    {"sk_bitmap_notify_pixels_changed", sk_bitmap_notify_pixels_changed}, // SkiaSharp
    {"sk_bitmap_peek_pixels", sk_bitmap_peek_pixels}, // SkiaSharp
    {"sk_bitmap_ready_to_draw", sk_bitmap_ready_to_draw}, // SkiaSharp
    {"sk_bitmap_reset", sk_bitmap_reset}, // SkiaSharp
    {"sk_bitmap_set_immutable", sk_bitmap_set_immutable}, // SkiaSharp
    {"sk_bitmap_set_pixels", sk_bitmap_set_pixels}, // SkiaSharp
    {"sk_bitmap_swap", sk_bitmap_swap}, // SkiaSharp
    {"sk_bitmap_try_alloc_pixels", sk_bitmap_try_alloc_pixels}, // SkiaSharp
    {"sk_bitmap_try_alloc_pixels_with_flags", sk_bitmap_try_alloc_pixels_with_flags}, // SkiaSharp
    {"sk_canvas_clear", sk_canvas_clear}, // SkiaSharp
    {"sk_canvas_clear_color4f", sk_canvas_clear_color4f}, // SkiaSharp
    {"sk_canvas_clip_path_with_operation", sk_canvas_clip_path_with_operation}, // SkiaSharp
    {"sk_canvas_clip_rect_with_operation", sk_canvas_clip_rect_with_operation}, // SkiaSharp
    {"sk_canvas_clip_region", sk_canvas_clip_region}, // SkiaSharp
    {"sk_canvas_clip_rrect_with_operation", sk_canvas_clip_rrect_with_operation}, // SkiaSharp
    {"sk_canvas_concat", sk_canvas_concat}, // SkiaSharp
    {"sk_canvas_destroy", sk_canvas_destroy}, // SkiaSharp
    {"sk_canvas_discard", sk_canvas_discard}, // SkiaSharp
    {"sk_canvas_draw_annotation", sk_canvas_draw_annotation}, // SkiaSharp
    {"sk_canvas_draw_arc", sk_canvas_draw_arc}, // SkiaSharp
    {"sk_canvas_draw_atlas", sk_canvas_draw_atlas}, // SkiaSharp
    {"sk_canvas_draw_circle", sk_canvas_draw_circle}, // SkiaSharp
    {"sk_canvas_draw_color", sk_canvas_draw_color}, // SkiaSharp
    {"sk_canvas_draw_color4f", sk_canvas_draw_color4f}, // SkiaSharp
    {"sk_canvas_draw_drawable", sk_canvas_draw_drawable}, // SkiaSharp
    {"sk_canvas_draw_drrect", sk_canvas_draw_drrect}, // SkiaSharp
    {"sk_canvas_draw_image", sk_canvas_draw_image}, // SkiaSharp
    {"sk_canvas_draw_image_lattice", sk_canvas_draw_image_lattice}, // SkiaSharp
    {"sk_canvas_draw_image_nine", sk_canvas_draw_image_nine}, // SkiaSharp
    {"sk_canvas_draw_image_rect", sk_canvas_draw_image_rect}, // SkiaSharp
    {"sk_canvas_draw_line", sk_canvas_draw_line}, // SkiaSharp
    {"sk_canvas_draw_link_destination_annotation", sk_canvas_draw_link_destination_annotation}, // SkiaSharp
    {"sk_canvas_draw_named_destination_annotation", sk_canvas_draw_named_destination_annotation}, // SkiaSharp
    {"sk_canvas_draw_oval", sk_canvas_draw_oval}, // SkiaSharp
    {"sk_canvas_draw_paint", sk_canvas_draw_paint}, // SkiaSharp
    {"sk_canvas_draw_patch", sk_canvas_draw_patch}, // SkiaSharp
    {"sk_canvas_draw_path", sk_canvas_draw_path}, // SkiaSharp
    {"sk_canvas_draw_picture", sk_canvas_draw_picture}, // SkiaSharp
    {"sk_canvas_draw_point", sk_canvas_draw_point}, // SkiaSharp
    {"sk_canvas_draw_points", sk_canvas_draw_points}, // SkiaSharp
    {"sk_canvas_draw_rect", sk_canvas_draw_rect}, // SkiaSharp
    {"sk_canvas_draw_region", sk_canvas_draw_region}, // SkiaSharp
    {"sk_canvas_draw_round_rect", sk_canvas_draw_round_rect}, // SkiaSharp
    {"sk_canvas_draw_rrect", sk_canvas_draw_rrect}, // SkiaSharp
    {"sk_canvas_draw_simple_text", sk_canvas_draw_simple_text}, // SkiaSharp
    {"sk_canvas_draw_text_blob", sk_canvas_draw_text_blob}, // SkiaSharp
    {"sk_canvas_draw_url_annotation", sk_canvas_draw_url_annotation}, // SkiaSharp
    {"sk_canvas_draw_vertices", sk_canvas_draw_vertices}, // SkiaSharp
    {"sk_canvas_flush", sk_canvas_flush}, // SkiaSharp
    {"sk_canvas_get_device_clip_bounds", sk_canvas_get_device_clip_bounds}, // SkiaSharp
    {"sk_canvas_get_local_clip_bounds", sk_canvas_get_local_clip_bounds}, // SkiaSharp
    {"sk_canvas_get_save_count", sk_canvas_get_save_count}, // SkiaSharp
    {"sk_canvas_get_total_matrix", sk_canvas_get_total_matrix}, // SkiaSharp
    {"sk_canvas_is_clip_empty", sk_canvas_is_clip_empty}, // SkiaSharp
    {"sk_canvas_is_clip_rect", sk_canvas_is_clip_rect}, // SkiaSharp
    {"sk_canvas_new_from_bitmap", sk_canvas_new_from_bitmap}, // SkiaSharp
    {"sk_canvas_quick_reject", sk_canvas_quick_reject}, // SkiaSharp
    {"sk_canvas_reset_matrix", sk_canvas_reset_matrix}, // SkiaSharp
    {"sk_canvas_restore", sk_canvas_restore}, // SkiaSharp
    {"sk_canvas_restore_to_count", sk_canvas_restore_to_count}, // SkiaSharp
    {"sk_canvas_rotate_degrees", sk_canvas_rotate_degrees}, // SkiaSharp
    {"sk_canvas_rotate_radians", sk_canvas_rotate_radians}, // SkiaSharp
    {"sk_canvas_save", sk_canvas_save}, // SkiaSharp
    {"sk_canvas_save_layer", sk_canvas_save_layer}, // SkiaSharp
    {"sk_canvas_scale", sk_canvas_scale}, // SkiaSharp
    {"sk_canvas_set_matrix", sk_canvas_set_matrix}, // SkiaSharp
    {"sk_canvas_skew", sk_canvas_skew}, // SkiaSharp
    {"sk_canvas_translate", sk_canvas_translate}, // SkiaSharp
    {"sk_codec_destroy", sk_codec_destroy}, // SkiaSharp
    {"sk_codec_get_encoded_format", sk_codec_get_encoded_format}, // SkiaSharp
    {"sk_codec_get_frame_count", sk_codec_get_frame_count}, // SkiaSharp
    {"sk_codec_get_frame_info", sk_codec_get_frame_info}, // SkiaSharp
    {"sk_codec_get_frame_info_for_index", sk_codec_get_frame_info_for_index}, // SkiaSharp
    {"sk_codec_get_info", sk_codec_get_info}, // SkiaSharp
    {"sk_codec_get_origin", sk_codec_get_origin}, // SkiaSharp
    {"sk_codec_get_pixels", sk_codec_get_pixels}, // SkiaSharp
    {"sk_codec_get_repetition_count", sk_codec_get_repetition_count}, // SkiaSharp
    {"sk_codec_get_scaled_dimensions", sk_codec_get_scaled_dimensions}, // SkiaSharp
    {"sk_codec_get_scanline_order", sk_codec_get_scanline_order}, // SkiaSharp
    {"sk_codec_get_scanlines", sk_codec_get_scanlines}, // SkiaSharp
    {"sk_codec_get_valid_subset", sk_codec_get_valid_subset}, // SkiaSharp
    {"sk_codec_incremental_decode", sk_codec_incremental_decode}, // SkiaSharp
    {"sk_codec_min_buffered_bytes_needed", sk_codec_min_buffered_bytes_needed}, // SkiaSharp
    {"sk_codec_new_from_data", sk_codec_new_from_data}, // SkiaSharp
    {"sk_codec_new_from_stream", sk_codec_new_from_stream}, // SkiaSharp
    {"sk_codec_next_scanline", sk_codec_next_scanline}, // SkiaSharp
    {"sk_codec_output_scanline", sk_codec_output_scanline}, // SkiaSharp
    {"sk_codec_skip_scanlines", sk_codec_skip_scanlines}, // SkiaSharp
    {"sk_codec_start_incremental_decode", sk_codec_start_incremental_decode}, // SkiaSharp
    {"sk_codec_start_scanline_decode", sk_codec_start_scanline_decode}, // SkiaSharp
    {"sk_color_get_bit_shift", sk_color_get_bit_shift}, // SkiaSharp
    {"sk_color_premultiply", sk_color_premultiply}, // SkiaSharp
    {"sk_color_premultiply_array", sk_color_premultiply_array}, // SkiaSharp
    {"sk_color_unpremultiply", sk_color_unpremultiply}, // SkiaSharp
    {"sk_color_unpremultiply_array", sk_color_unpremultiply_array}, // SkiaSharp
    {"sk_color4f_from_color", sk_color4f_from_color}, // SkiaSharp
    {"sk_color4f_to_color", sk_color4f_to_color}, // SkiaSharp
    {"sk_colorfilter_new_color_matrix", sk_colorfilter_new_color_matrix}, // SkiaSharp
    {"sk_colorfilter_new_compose", sk_colorfilter_new_compose}, // SkiaSharp
    {"sk_colorfilter_new_high_contrast", sk_colorfilter_new_high_contrast}, // SkiaSharp
    {"sk_colorfilter_new_lighting", sk_colorfilter_new_lighting}, // SkiaSharp
    {"sk_colorfilter_new_luma_color", sk_colorfilter_new_luma_color}, // SkiaSharp
    {"sk_colorfilter_new_mode", sk_colorfilter_new_mode}, // SkiaSharp
    {"sk_colorfilter_new_table", sk_colorfilter_new_table}, // SkiaSharp
    {"sk_colorfilter_new_table_argb", sk_colorfilter_new_table_argb}, // SkiaSharp
    {"sk_colorfilter_unref", sk_colorfilter_unref}, // SkiaSharp
    {"sk_colorspace_equals", sk_colorspace_equals}, // SkiaSharp
    {"sk_colorspace_gamma_close_to_srgb", sk_colorspace_gamma_close_to_srgb}, // SkiaSharp
    {"sk_colorspace_gamma_is_linear", sk_colorspace_gamma_is_linear}, // SkiaSharp
    {"sk_colorspace_icc_profile_delete", sk_colorspace_icc_profile_delete}, // SkiaSharp
    {"sk_colorspace_icc_profile_get_buffer", sk_colorspace_icc_profile_get_buffer}, // SkiaSharp
    {"sk_colorspace_icc_profile_get_to_xyzd50", sk_colorspace_icc_profile_get_to_xyzd50}, // SkiaSharp
    {"sk_colorspace_icc_profile_new", sk_colorspace_icc_profile_new}, // SkiaSharp
    {"sk_colorspace_icc_profile_parse", sk_colorspace_icc_profile_parse}, // SkiaSharp
    {"sk_colorspace_is_numerical_transfer_fn", sk_colorspace_is_numerical_transfer_fn}, // SkiaSharp
    {"sk_colorspace_is_srgb", sk_colorspace_is_srgb}, // SkiaSharp
    {"sk_colorspace_make_linear_gamma", sk_colorspace_make_linear_gamma}, // SkiaSharp
    {"sk_colorspace_make_srgb_gamma", sk_colorspace_make_srgb_gamma}, // SkiaSharp
    {"sk_colorspace_new_icc", sk_colorspace_new_icc}, // SkiaSharp
    {"sk_colorspace_new_rgb", sk_colorspace_new_rgb}, // SkiaSharp
    {"sk_colorspace_new_srgb", sk_colorspace_new_srgb}, // SkiaSharp
    {"sk_colorspace_new_srgb_linear", sk_colorspace_new_srgb_linear}, // SkiaSharp
    {"sk_colorspace_primaries_to_xyzd50", sk_colorspace_primaries_to_xyzd50}, // SkiaSharp
    {"sk_colorspace_ref", sk_colorspace_ref}, // SkiaSharp
    {"sk_colorspace_to_profile", sk_colorspace_to_profile}, // SkiaSharp
    {"sk_colorspace_to_xyzd50", sk_colorspace_to_xyzd50}, // SkiaSharp
    {"sk_colorspace_transfer_fn_eval", sk_colorspace_transfer_fn_eval}, // SkiaSharp
    {"sk_colorspace_transfer_fn_invert", sk_colorspace_transfer_fn_invert}, // SkiaSharp
    {"sk_colorspace_transfer_fn_named_2dot2", sk_colorspace_transfer_fn_named_2dot2}, // SkiaSharp
    {"sk_colorspace_transfer_fn_named_hlg", sk_colorspace_transfer_fn_named_hlg}, // SkiaSharp
    {"sk_colorspace_transfer_fn_named_linear", sk_colorspace_transfer_fn_named_linear}, // SkiaSharp
    {"sk_colorspace_transfer_fn_named_pq", sk_colorspace_transfer_fn_named_pq}, // SkiaSharp
    {"sk_colorspace_transfer_fn_named_rec2020", sk_colorspace_transfer_fn_named_rec2020}, // SkiaSharp
    {"sk_colorspace_transfer_fn_named_srgb", sk_colorspace_transfer_fn_named_srgb}, // SkiaSharp
    {"sk_colorspace_unref", sk_colorspace_unref}, // SkiaSharp
    {"sk_colorspace_xyz_concat", sk_colorspace_xyz_concat}, // SkiaSharp
    {"sk_colorspace_xyz_invert", sk_colorspace_xyz_invert}, // SkiaSharp
    {"sk_colorspace_xyz_named_adobe_rgb", sk_colorspace_xyz_named_adobe_rgb}, // SkiaSharp
    {"sk_colorspace_xyz_named_display_p3", sk_colorspace_xyz_named_display_p3}, // SkiaSharp
    {"sk_colorspace_xyz_named_rec2020", sk_colorspace_xyz_named_rec2020}, // SkiaSharp
    {"sk_colorspace_xyz_named_srgb", sk_colorspace_xyz_named_srgb}, // SkiaSharp
    {"sk_colorspace_xyz_named_xyz", sk_colorspace_xyz_named_xyz}, // SkiaSharp
    {"sk_colortable_count", sk_colortable_count}, // SkiaSharp
    {"sk_colortable_new", sk_colortable_new}, // SkiaSharp
    {"sk_colortable_read_colors", sk_colortable_read_colors}, // SkiaSharp
    {"sk_colortable_unref", sk_colortable_unref}, // SkiaSharp
    {"sk_colortype_get_default_8888", sk_colortype_get_default_8888}, // SkiaSharp
    {"sk_compatpaint_clone", sk_compatpaint_clone}, // SkiaSharp
    {"sk_compatpaint_delete", sk_compatpaint_delete}, // SkiaSharp
    {"sk_compatpaint_get_font", sk_compatpaint_get_font}, // SkiaSharp
    {"sk_compatpaint_get_text_align", sk_compatpaint_get_text_align}, // SkiaSharp
    {"sk_compatpaint_get_text_encoding", sk_compatpaint_get_text_encoding}, // SkiaSharp
    {"sk_compatpaint_make_font", sk_compatpaint_make_font}, // SkiaSharp
    {"sk_compatpaint_new", sk_compatpaint_new}, // SkiaSharp
    {"sk_compatpaint_new_with_font", sk_compatpaint_new_with_font}, // SkiaSharp
    {"sk_compatpaint_reset", sk_compatpaint_reset}, // SkiaSharp
    {"sk_compatpaint_set_text_align", sk_compatpaint_set_text_align}, // SkiaSharp
    {"sk_compatpaint_set_text_encoding", sk_compatpaint_set_text_encoding}, // SkiaSharp
    {"sk_data_get_bytes", sk_data_get_bytes}, // SkiaSharp
    {"sk_data_get_data", sk_data_get_data}, // SkiaSharp
    {"sk_data_get_size", sk_data_get_size}, // SkiaSharp
    {"sk_data_new_empty", sk_data_new_empty}, // SkiaSharp
    {"sk_data_new_from_file", sk_data_new_from_file}, // SkiaSharp
    {"sk_data_new_from_stream", sk_data_new_from_stream}, // SkiaSharp
    {"sk_data_new_subset", sk_data_new_subset}, // SkiaSharp
    {"sk_data_new_uninitialized", sk_data_new_uninitialized}, // SkiaSharp
    {"sk_data_new_with_copy", sk_data_new_with_copy}, // SkiaSharp
    {"sk_data_new_with_proc", sk_data_new_with_proc}, // SkiaSharp
    {"sk_data_ref", sk_data_ref}, // SkiaSharp
    {"sk_data_unref", sk_data_unref}, // SkiaSharp
    {"sk_document_abort", sk_document_abort}, // SkiaSharp
    {"sk_document_begin_page", sk_document_begin_page}, // SkiaSharp
    {"sk_document_close", sk_document_close}, // SkiaSharp
    {"sk_document_create_pdf_from_stream", sk_document_create_pdf_from_stream}, // SkiaSharp
    {"sk_document_create_pdf_from_stream_with_metadata", sk_document_create_pdf_from_stream_with_metadata}, // SkiaSharp
    {"sk_document_create_xps_from_stream", sk_document_create_xps_from_stream}, // SkiaSharp
    {"sk_document_end_page", sk_document_end_page}, // SkiaSharp
    {"sk_document_unref", sk_document_unref}, // SkiaSharp
    {"sk_drawable_draw", sk_drawable_draw}, // SkiaSharp
    {"sk_drawable_get_bounds", sk_drawable_get_bounds}, // SkiaSharp
    {"sk_drawable_get_generation_id", sk_drawable_get_generation_id}, // SkiaSharp
    {"sk_drawable_new_picture_snapshot", sk_drawable_new_picture_snapshot}, // SkiaSharp
    {"sk_drawable_notify_drawing_changed", sk_drawable_notify_drawing_changed}, // SkiaSharp
    {"sk_drawable_unref", sk_drawable_unref}, // SkiaSharp
    {"sk_dynamicmemorywstream_copy_to", sk_dynamicmemorywstream_copy_to}, // SkiaSharp
    {"sk_dynamicmemorywstream_destroy", sk_dynamicmemorywstream_destroy}, // SkiaSharp
    {"sk_dynamicmemorywstream_detach_as_data", sk_dynamicmemorywstream_detach_as_data}, // SkiaSharp
    {"sk_dynamicmemorywstream_detach_as_stream", sk_dynamicmemorywstream_detach_as_stream}, // SkiaSharp
    {"sk_dynamicmemorywstream_new", sk_dynamicmemorywstream_new}, // SkiaSharp
    {"sk_dynamicmemorywstream_write_to_stream", sk_dynamicmemorywstream_write_to_stream}, // SkiaSharp
    {"sk_filestream_destroy", sk_filestream_destroy}, // SkiaSharp
    {"sk_filestream_is_valid", sk_filestream_is_valid}, // SkiaSharp
    {"sk_filestream_new", sk_filestream_new}, // SkiaSharp
    {"sk_filewstream_destroy", sk_filewstream_destroy}, // SkiaSharp
    {"sk_filewstream_is_valid", sk_filewstream_is_valid}, // SkiaSharp
    {"sk_filewstream_new", sk_filewstream_new}, // SkiaSharp
    {"sk_font_break_text", sk_font_break_text}, // SkiaSharp
    {"sk_font_delete", sk_font_delete}, // SkiaSharp
    {"sk_font_get_edging", sk_font_get_edging}, // SkiaSharp
    {"sk_font_get_hinting", sk_font_get_hinting}, // SkiaSharp
    {"sk_font_get_metrics", sk_font_get_metrics}, // SkiaSharp
    {"sk_font_get_path", sk_font_get_path}, // SkiaSharp
    {"sk_font_get_paths", sk_font_get_paths}, // SkiaSharp
    {"sk_font_get_pos", sk_font_get_pos}, // SkiaSharp
    {"sk_font_get_scale_x", sk_font_get_scale_x}, // SkiaSharp
    {"sk_font_get_size", sk_font_get_size}, // SkiaSharp
    {"sk_font_get_skew_x", sk_font_get_skew_x}, // SkiaSharp
    {"sk_font_get_typeface", sk_font_get_typeface}, // SkiaSharp
    {"sk_font_get_widths_bounds", sk_font_get_widths_bounds}, // SkiaSharp
    {"sk_font_get_xpos", sk_font_get_xpos}, // SkiaSharp
    {"sk_font_is_baseline_snap", sk_font_is_baseline_snap}, // SkiaSharp
    {"sk_font_is_embedded_bitmaps", sk_font_is_embedded_bitmaps}, // SkiaSharp
    {"sk_font_is_embolden", sk_font_is_embolden}, // SkiaSharp
    {"sk_font_is_force_auto_hinting", sk_font_is_force_auto_hinting}, // SkiaSharp
    {"sk_font_is_linear_metrics", sk_font_is_linear_metrics}, // SkiaSharp
    {"sk_font_is_subpixel", sk_font_is_subpixel}, // SkiaSharp
    {"sk_font_measure_text", sk_font_measure_text}, // SkiaSharp
    {"sk_font_measure_text_no_return", sk_font_measure_text_no_return}, // SkiaSharp
    {"sk_font_new", sk_font_new}, // SkiaSharp
    {"sk_font_new_with_values", sk_font_new_with_values}, // SkiaSharp
    {"sk_font_set_baseline_snap", sk_font_set_baseline_snap}, // SkiaSharp
    {"sk_font_set_edging", sk_font_set_edging}, // SkiaSharp
    {"sk_font_set_embedded_bitmaps", sk_font_set_embedded_bitmaps}, // SkiaSharp
    {"sk_font_set_embolden", sk_font_set_embolden}, // SkiaSharp
    {"sk_font_set_force_auto_hinting", sk_font_set_force_auto_hinting}, // SkiaSharp
    {"sk_font_set_hinting", sk_font_set_hinting}, // SkiaSharp
    {"sk_font_set_linear_metrics", sk_font_set_linear_metrics}, // SkiaSharp
    {"sk_font_set_scale_x", sk_font_set_scale_x}, // SkiaSharp
    {"sk_font_set_size", sk_font_set_size}, // SkiaSharp
    {"sk_font_set_skew_x", sk_font_set_skew_x}, // SkiaSharp
    {"sk_font_set_subpixel", sk_font_set_subpixel}, // SkiaSharp
    {"sk_font_set_typeface", sk_font_set_typeface}, // SkiaSharp
    {"sk_font_text_to_glyphs", sk_font_text_to_glyphs}, // SkiaSharp
    {"sk_font_unichar_to_glyph", sk_font_unichar_to_glyph}, // SkiaSharp
    {"sk_font_unichars_to_glyphs", sk_font_unichars_to_glyphs}, // SkiaSharp
    {"sk_fontmgr_count_families", sk_fontmgr_count_families}, // SkiaSharp
    {"sk_fontmgr_create_default", sk_fontmgr_create_default}, // SkiaSharp
    {"sk_fontmgr_create_from_data", sk_fontmgr_create_from_data}, // SkiaSharp
    {"sk_fontmgr_create_from_file", sk_fontmgr_create_from_file}, // SkiaSharp
    {"sk_fontmgr_create_from_stream", sk_fontmgr_create_from_stream}, // SkiaSharp
    {"sk_fontmgr_create_styleset", sk_fontmgr_create_styleset}, // SkiaSharp
    {"sk_fontmgr_get_family_name", sk_fontmgr_get_family_name}, // SkiaSharp
    {"sk_fontmgr_match_face_style", sk_fontmgr_match_face_style}, // SkiaSharp
    {"sk_fontmgr_match_family", sk_fontmgr_match_family}, // SkiaSharp
    {"sk_fontmgr_match_family_style", sk_fontmgr_match_family_style}, // SkiaSharp
    {"sk_fontmgr_match_family_style_character", sk_fontmgr_match_family_style_character}, // SkiaSharp
    {"sk_fontmgr_ref_default", sk_fontmgr_ref_default}, // SkiaSharp
    {"sk_fontmgr_unref", sk_fontmgr_unref}, // SkiaSharp
    {"sk_fontstyle_delete", sk_fontstyle_delete}, // SkiaSharp
    {"sk_fontstyle_get_slant", sk_fontstyle_get_slant}, // SkiaSharp
    {"sk_fontstyle_get_weight", sk_fontstyle_get_weight}, // SkiaSharp
    {"sk_fontstyle_get_width", sk_fontstyle_get_width}, // SkiaSharp
    {"sk_fontstyle_new", sk_fontstyle_new}, // SkiaSharp
    {"sk_fontstyleset_create_empty", sk_fontstyleset_create_empty}, // SkiaSharp
    {"sk_fontstyleset_create_typeface", sk_fontstyleset_create_typeface}, // SkiaSharp
    {"sk_fontstyleset_get_count", sk_fontstyleset_get_count}, // SkiaSharp
    {"sk_fontstyleset_get_style", sk_fontstyleset_get_style}, // SkiaSharp
    {"sk_fontstyleset_match_style", sk_fontstyleset_match_style}, // SkiaSharp
    {"sk_fontstyleset_unref", sk_fontstyleset_unref}, // SkiaSharp
    {"sk_graphics_dump_memory_statistics", sk_graphics_dump_memory_statistics}, // SkiaSharp
    {"sk_graphics_get_font_cache_count_limit", sk_graphics_get_font_cache_count_limit}, // SkiaSharp
    {"sk_graphics_get_font_cache_count_used", sk_graphics_get_font_cache_count_used}, // SkiaSharp
    {"sk_graphics_get_font_cache_limit", sk_graphics_get_font_cache_limit}, // SkiaSharp
    {"sk_graphics_get_font_cache_point_size_limit", sk_graphics_get_font_cache_point_size_limit}, // SkiaSharp
    {"sk_graphics_get_font_cache_used", sk_graphics_get_font_cache_used}, // SkiaSharp
    {"sk_graphics_get_resource_cache_single_allocation_byte_limit", sk_graphics_get_resource_cache_single_allocation_byte_limit}, // SkiaSharp
    {"sk_graphics_get_resource_cache_total_byte_limit", sk_graphics_get_resource_cache_total_byte_limit}, // SkiaSharp
    {"sk_graphics_get_resource_cache_total_bytes_used", sk_graphics_get_resource_cache_total_bytes_used}, // SkiaSharp
    {"sk_graphics_init", sk_graphics_init}, // SkiaSharp
    {"sk_graphics_purge_all_caches", sk_graphics_purge_all_caches}, // SkiaSharp
    {"sk_graphics_purge_font_cache", sk_graphics_purge_font_cache}, // SkiaSharp
    {"sk_graphics_purge_resource_cache", sk_graphics_purge_resource_cache}, // SkiaSharp
    {"sk_graphics_set_font_cache_count_limit", sk_graphics_set_font_cache_count_limit}, // SkiaSharp
    {"sk_graphics_set_font_cache_limit", sk_graphics_set_font_cache_limit}, // SkiaSharp
    {"sk_graphics_set_font_cache_point_size_limit", sk_graphics_set_font_cache_point_size_limit}, // SkiaSharp
    {"sk_graphics_set_resource_cache_single_allocation_byte_limit", sk_graphics_set_resource_cache_single_allocation_byte_limit}, // SkiaSharp
    {"sk_graphics_set_resource_cache_total_byte_limit", sk_graphics_set_resource_cache_total_byte_limit}, // SkiaSharp
    {"sk_image_encode", sk_image_encode}, // SkiaSharp
    {"sk_image_encode_specific", sk_image_encode_specific}, // SkiaSharp
    {"sk_image_get_alpha_type", sk_image_get_alpha_type}, // SkiaSharp
    {"sk_image_get_color_type", sk_image_get_color_type}, // SkiaSharp
    {"sk_image_get_colorspace", sk_image_get_colorspace}, // SkiaSharp
    {"sk_image_get_height", sk_image_get_height}, // SkiaSharp
    {"sk_image_get_unique_id", sk_image_get_unique_id}, // SkiaSharp
    {"sk_image_get_width", sk_image_get_width}, // SkiaSharp
    {"sk_image_is_alpha_only", sk_image_is_alpha_only}, // SkiaSharp
    {"sk_image_is_lazy_generated", sk_image_is_lazy_generated}, // SkiaSharp
    {"sk_image_is_texture_backed", sk_image_is_texture_backed}, // SkiaSharp
    {"sk_image_is_valid", sk_image_is_valid}, // SkiaSharp
    {"sk_image_make_non_texture_image", sk_image_make_non_texture_image}, // SkiaSharp
    {"sk_image_make_raster_image", sk_image_make_raster_image}, // SkiaSharp
    {"sk_image_make_shader", sk_image_make_shader}, // SkiaSharp
    {"sk_image_make_subset", sk_image_make_subset}, // SkiaSharp
    {"sk_image_make_texture_image", sk_image_make_texture_image}, // SkiaSharp
    {"sk_image_make_with_filter", sk_image_make_with_filter}, // SkiaSharp
    {"sk_image_make_with_filter_legacy", sk_image_make_with_filter_legacy}, // SkiaSharp
    {"sk_image_new_from_adopted_texture", sk_image_new_from_adopted_texture}, // SkiaSharp
    {"sk_image_new_from_bitmap", sk_image_new_from_bitmap}, // SkiaSharp
    {"sk_image_new_from_encoded", sk_image_new_from_encoded}, // SkiaSharp
    {"sk_image_new_from_picture", sk_image_new_from_picture}, // SkiaSharp
    {"sk_image_new_from_texture", sk_image_new_from_texture}, // SkiaSharp
    {"sk_image_new_raster", sk_image_new_raster}, // SkiaSharp
    {"sk_image_new_raster_copy", sk_image_new_raster_copy}, // SkiaSharp
    {"sk_image_new_raster_copy_with_pixmap", sk_image_new_raster_copy_with_pixmap}, // SkiaSharp
    {"sk_image_new_raster_data", sk_image_new_raster_data}, // SkiaSharp
    {"sk_image_peek_pixels", sk_image_peek_pixels}, // SkiaSharp
    {"sk_image_read_pixels", sk_image_read_pixels}, // SkiaSharp
    {"sk_image_read_pixels_into_pixmap", sk_image_read_pixels_into_pixmap}, // SkiaSharp
    {"sk_image_ref", sk_image_ref}, // SkiaSharp
    {"sk_image_ref_encoded", sk_image_ref_encoded}, // SkiaSharp
    {"sk_image_scale_pixels", sk_image_scale_pixels}, // SkiaSharp
    {"sk_image_unref", sk_image_unref}, // SkiaSharp
    {"sk_imagefilter_croprect_destructor", sk_imagefilter_croprect_destructor}, // SkiaSharp
    {"sk_imagefilter_croprect_get_flags", sk_imagefilter_croprect_get_flags}, // SkiaSharp
    {"sk_imagefilter_croprect_get_rect", sk_imagefilter_croprect_get_rect}, // SkiaSharp
    {"sk_imagefilter_croprect_new", sk_imagefilter_croprect_new}, // SkiaSharp
    {"sk_imagefilter_croprect_new_with_rect", sk_imagefilter_croprect_new_with_rect}, // SkiaSharp
    {"sk_imagefilter_new_alpha_threshold", sk_imagefilter_new_alpha_threshold}, // SkiaSharp
    {"sk_imagefilter_new_arithmetic", sk_imagefilter_new_arithmetic}, // SkiaSharp
    {"sk_imagefilter_new_blur", sk_imagefilter_new_blur}, // SkiaSharp
    {"sk_imagefilter_new_color_filter", sk_imagefilter_new_color_filter}, // SkiaSharp
    {"sk_imagefilter_new_compose", sk_imagefilter_new_compose}, // SkiaSharp
    {"sk_imagefilter_new_dilate", sk_imagefilter_new_dilate}, // SkiaSharp
    {"sk_imagefilter_new_displacement_map_effect", sk_imagefilter_new_displacement_map_effect}, // SkiaSharp
    {"sk_imagefilter_new_distant_lit_diffuse", sk_imagefilter_new_distant_lit_diffuse}, // SkiaSharp
    {"sk_imagefilter_new_distant_lit_specular", sk_imagefilter_new_distant_lit_specular}, // SkiaSharp
    {"sk_imagefilter_new_drop_shadow", sk_imagefilter_new_drop_shadow}, // SkiaSharp
    {"sk_imagefilter_new_drop_shadow_only", sk_imagefilter_new_drop_shadow_only}, // SkiaSharp
    {"sk_imagefilter_new_erode", sk_imagefilter_new_erode}, // SkiaSharp
    {"sk_imagefilter_new_image_source", sk_imagefilter_new_image_source}, // SkiaSharp
    {"sk_imagefilter_new_image_source_default", sk_imagefilter_new_image_source_default}, // SkiaSharp
    {"sk_imagefilter_new_magnifier", sk_imagefilter_new_magnifier}, // SkiaSharp
    {"sk_imagefilter_new_matrix", sk_imagefilter_new_matrix}, // SkiaSharp
    {"sk_imagefilter_new_matrix_convolution", sk_imagefilter_new_matrix_convolution}, // SkiaSharp
    {"sk_imagefilter_new_merge", sk_imagefilter_new_merge}, // SkiaSharp
    {"sk_imagefilter_new_offset", sk_imagefilter_new_offset}, // SkiaSharp
    {"sk_imagefilter_new_paint", sk_imagefilter_new_paint}, // SkiaSharp
    {"sk_imagefilter_new_picture", sk_imagefilter_new_picture}, // SkiaSharp
    {"sk_imagefilter_new_picture_with_croprect", sk_imagefilter_new_picture_with_croprect}, // SkiaSharp
    {"sk_imagefilter_new_point_lit_diffuse", sk_imagefilter_new_point_lit_diffuse}, // SkiaSharp
    {"sk_imagefilter_new_point_lit_specular", sk_imagefilter_new_point_lit_specular}, // SkiaSharp
    {"sk_imagefilter_new_spot_lit_diffuse", sk_imagefilter_new_spot_lit_diffuse}, // SkiaSharp
    {"sk_imagefilter_new_spot_lit_specular", sk_imagefilter_new_spot_lit_specular}, // SkiaSharp
    {"sk_imagefilter_new_tile", sk_imagefilter_new_tile}, // SkiaSharp
    {"sk_imagefilter_new_xfermode", sk_imagefilter_new_xfermode}, // SkiaSharp
    {"sk_imagefilter_unref", sk_imagefilter_unref}, // SkiaSharp
    {"sk_jpegencoder_encode", sk_jpegencoder_encode}, // SkiaSharp
    {"sk_manageddrawable_new", sk_manageddrawable_new}, // SkiaSharp
    {"sk_manageddrawable_set_procs", sk_manageddrawable_set_procs}, // SkiaSharp
    {"sk_manageddrawable_unref", sk_manageddrawable_unref}, // SkiaSharp
    {"sk_managedstream_destroy", sk_managedstream_destroy}, // SkiaSharp
    {"sk_managedstream_new", sk_managedstream_new}, // SkiaSharp
    {"sk_managedstream_set_procs", sk_managedstream_set_procs}, // SkiaSharp
    {"sk_managedtracememorydump_delete", sk_managedtracememorydump_delete}, // SkiaSharp
    {"sk_managedtracememorydump_new", sk_managedtracememorydump_new}, // SkiaSharp
    {"sk_managedtracememorydump_set_procs", sk_managedtracememorydump_set_procs}, // SkiaSharp
    {"sk_managedwstream_destroy", sk_managedwstream_destroy}, // SkiaSharp
    {"sk_managedwstream_new", sk_managedwstream_new}, // SkiaSharp
    {"sk_managedwstream_set_procs", sk_managedwstream_set_procs}, // SkiaSharp
    {"sk_mask_alloc_image", sk_mask_alloc_image}, // SkiaSharp
    {"sk_mask_compute_image_size", sk_mask_compute_image_size}, // SkiaSharp
    {"sk_mask_compute_total_image_size", sk_mask_compute_total_image_size}, // SkiaSharp
    {"sk_mask_free_image", sk_mask_free_image}, // SkiaSharp
    {"sk_mask_get_addr", sk_mask_get_addr}, // SkiaSharp
    {"sk_mask_get_addr_1", sk_mask_get_addr_1}, // SkiaSharp
    {"sk_mask_get_addr_32", sk_mask_get_addr_32}, // SkiaSharp
    {"sk_mask_get_addr_8", sk_mask_get_addr_8}, // SkiaSharp
    {"sk_mask_get_addr_lcd_16", sk_mask_get_addr_lcd_16}, // SkiaSharp
    {"sk_mask_is_empty", sk_mask_is_empty}, // SkiaSharp
    {"sk_maskfilter_new_blur", sk_maskfilter_new_blur}, // SkiaSharp
    {"sk_maskfilter_new_blur_with_flags", sk_maskfilter_new_blur_with_flags}, // SkiaSharp
    {"sk_maskfilter_new_clip", sk_maskfilter_new_clip}, // SkiaSharp
    {"sk_maskfilter_new_gamma", sk_maskfilter_new_gamma}, // SkiaSharp
    {"sk_maskfilter_new_shader", sk_maskfilter_new_shader}, // SkiaSharp
    {"sk_maskfilter_new_table", sk_maskfilter_new_table}, // SkiaSharp
    {"sk_maskfilter_ref", sk_maskfilter_ref}, // SkiaSharp
    {"sk_maskfilter_unref", sk_maskfilter_unref}, // SkiaSharp
    {"sk_matrix_concat", sk_matrix_concat}, // SkiaSharp
    {"sk_matrix_map_points", sk_matrix_map_points}, // SkiaSharp
    {"sk_matrix_map_radius", sk_matrix_map_radius}, // SkiaSharp
    {"sk_matrix_map_rect", sk_matrix_map_rect}, // SkiaSharp
    {"sk_matrix_map_vector", sk_matrix_map_vector}, // SkiaSharp
    {"sk_matrix_map_vectors", sk_matrix_map_vectors}, // SkiaSharp
    {"sk_matrix_map_xy", sk_matrix_map_xy}, // SkiaSharp
    {"sk_matrix_post_concat", sk_matrix_post_concat}, // SkiaSharp
    {"sk_matrix_pre_concat", sk_matrix_pre_concat}, // SkiaSharp
    {"sk_matrix_try_invert", sk_matrix_try_invert}, // SkiaSharp
    {"sk_matrix44_as_col_major", sk_matrix44_as_col_major}, // SkiaSharp
    {"sk_matrix44_as_row_major", sk_matrix44_as_row_major}, // SkiaSharp
    {"sk_matrix44_destroy", sk_matrix44_destroy}, // SkiaSharp
    {"sk_matrix44_determinant", sk_matrix44_determinant}, // SkiaSharp
    {"sk_matrix44_equals", sk_matrix44_equals}, // SkiaSharp
    {"sk_matrix44_get", sk_matrix44_get}, // SkiaSharp
    {"sk_matrix44_get_type", sk_matrix44_get_type}, // SkiaSharp
    {"sk_matrix44_invert", sk_matrix44_invert}, // SkiaSharp
    {"sk_matrix44_map_scalars", sk_matrix44_map_scalars}, // SkiaSharp
    {"sk_matrix44_map2", sk_matrix44_map2}, // SkiaSharp
    {"sk_matrix44_new", sk_matrix44_new}, // SkiaSharp
    {"sk_matrix44_new_concat", sk_matrix44_new_concat}, // SkiaSharp
    {"sk_matrix44_new_copy", sk_matrix44_new_copy}, // SkiaSharp
    {"sk_matrix44_new_identity", sk_matrix44_new_identity}, // SkiaSharp
    {"sk_matrix44_new_matrix", sk_matrix44_new_matrix}, // SkiaSharp
    {"sk_matrix44_post_concat", sk_matrix44_post_concat}, // SkiaSharp
    {"sk_matrix44_post_scale", sk_matrix44_post_scale}, // SkiaSharp
    {"sk_matrix44_post_translate", sk_matrix44_post_translate}, // SkiaSharp
    {"sk_matrix44_pre_concat", sk_matrix44_pre_concat}, // SkiaSharp
    {"sk_matrix44_pre_scale", sk_matrix44_pre_scale}, // SkiaSharp
    {"sk_matrix44_pre_translate", sk_matrix44_pre_translate}, // SkiaSharp
    {"sk_matrix44_preserves_2d_axis_alignment", sk_matrix44_preserves_2d_axis_alignment}, // SkiaSharp
    {"sk_matrix44_set", sk_matrix44_set}, // SkiaSharp
    {"sk_matrix44_set_3x3_row_major", sk_matrix44_set_3x3_row_major}, // SkiaSharp
    {"sk_matrix44_set_col_major", sk_matrix44_set_col_major}, // SkiaSharp
    {"sk_matrix44_set_concat", sk_matrix44_set_concat}, // SkiaSharp
    {"sk_matrix44_set_identity", sk_matrix44_set_identity}, // SkiaSharp
    {"sk_matrix44_set_rotate_about_degrees", sk_matrix44_set_rotate_about_degrees}, // SkiaSharp
    {"sk_matrix44_set_rotate_about_radians", sk_matrix44_set_rotate_about_radians}, // SkiaSharp
    {"sk_matrix44_set_rotate_about_radians_unit", sk_matrix44_set_rotate_about_radians_unit}, // SkiaSharp
    {"sk_matrix44_set_row_major", sk_matrix44_set_row_major}, // SkiaSharp
    {"sk_matrix44_set_scale", sk_matrix44_set_scale}, // SkiaSharp
    {"sk_matrix44_set_translate", sk_matrix44_set_translate}, // SkiaSharp
    {"sk_matrix44_to_matrix", sk_matrix44_to_matrix}, // SkiaSharp
    {"sk_matrix44_transpose", sk_matrix44_transpose}, // SkiaSharp
    {"sk_memorystream_destroy", sk_memorystream_destroy}, // SkiaSharp
    {"sk_memorystream_new", sk_memorystream_new}, // SkiaSharp
    {"sk_memorystream_new_with_data", sk_memorystream_new_with_data}, // SkiaSharp
    {"sk_memorystream_new_with_length", sk_memorystream_new_with_length}, // SkiaSharp
    {"sk_memorystream_new_with_skdata", sk_memorystream_new_with_skdata}, // SkiaSharp
    {"sk_memorystream_set_memory", sk_memorystream_set_memory}, // SkiaSharp
    {"sk_nodraw_canvas_destroy", sk_nodraw_canvas_destroy}, // SkiaSharp
    {"sk_nodraw_canvas_new", sk_nodraw_canvas_new}, // SkiaSharp
    {"sk_nvrefcnt_get_ref_count", sk_nvrefcnt_get_ref_count}, // SkiaSharp
    {"sk_nvrefcnt_safe_ref", sk_nvrefcnt_safe_ref}, // SkiaSharp
    {"sk_nvrefcnt_safe_unref", sk_nvrefcnt_safe_unref}, // SkiaSharp
    {"sk_nvrefcnt_unique", sk_nvrefcnt_unique}, // SkiaSharp
    {"sk_nway_canvas_add_canvas", sk_nway_canvas_add_canvas}, // SkiaSharp
    {"sk_nway_canvas_destroy", sk_nway_canvas_destroy}, // SkiaSharp
    {"sk_nway_canvas_new", sk_nway_canvas_new}, // SkiaSharp
    {"sk_nway_canvas_remove_all", sk_nway_canvas_remove_all}, // SkiaSharp
    {"sk_nway_canvas_remove_canvas", sk_nway_canvas_remove_canvas}, // SkiaSharp
    {"sk_opbuilder_add", sk_opbuilder_add}, // SkiaSharp
    {"sk_opbuilder_destroy", sk_opbuilder_destroy}, // SkiaSharp
    {"sk_opbuilder_new", sk_opbuilder_new}, // SkiaSharp
    {"sk_opbuilder_resolve", sk_opbuilder_resolve}, // SkiaSharp
    {"sk_overdraw_canvas_destroy", sk_overdraw_canvas_destroy}, // SkiaSharp
    {"sk_overdraw_canvas_new", sk_overdraw_canvas_new}, // SkiaSharp
    {"sk_paint_clone", sk_paint_clone}, // SkiaSharp
    {"sk_paint_delete", sk_paint_delete}, // SkiaSharp
    {"sk_paint_get_blendmode", sk_paint_get_blendmode}, // SkiaSharp
    {"sk_paint_get_color", sk_paint_get_color}, // SkiaSharp
    {"sk_paint_get_color4f", sk_paint_get_color4f}, // SkiaSharp
    {"sk_paint_get_colorfilter", sk_paint_get_colorfilter}, // SkiaSharp
    {"sk_paint_get_fill_path", sk_paint_get_fill_path}, // SkiaSharp
    {"sk_paint_get_filter_quality", sk_paint_get_filter_quality}, // SkiaSharp
    {"sk_paint_get_imagefilter", sk_paint_get_imagefilter}, // SkiaSharp
    {"sk_paint_get_maskfilter", sk_paint_get_maskfilter}, // SkiaSharp
    {"sk_paint_get_path_effect", sk_paint_get_path_effect}, // SkiaSharp
    {"sk_paint_get_shader", sk_paint_get_shader}, // SkiaSharp
    {"sk_paint_get_stroke_cap", sk_paint_get_stroke_cap}, // SkiaSharp
    {"sk_paint_get_stroke_join", sk_paint_get_stroke_join}, // SkiaSharp
    {"sk_paint_get_stroke_miter", sk_paint_get_stroke_miter}, // SkiaSharp
    {"sk_paint_get_stroke_width", sk_paint_get_stroke_width}, // SkiaSharp
    {"sk_paint_get_style", sk_paint_get_style}, // SkiaSharp
    {"sk_paint_is_antialias", sk_paint_is_antialias}, // SkiaSharp
    {"sk_paint_is_dither", sk_paint_is_dither}, // SkiaSharp
    {"sk_paint_new", sk_paint_new}, // SkiaSharp
    {"sk_paint_reset", sk_paint_reset}, // SkiaSharp
    {"sk_paint_set_antialias", sk_paint_set_antialias}, // SkiaSharp
    {"sk_paint_set_blendmode", sk_paint_set_blendmode}, // SkiaSharp
    {"sk_paint_set_color", sk_paint_set_color}, // SkiaSharp
    {"sk_paint_set_color4f", sk_paint_set_color4f}, // SkiaSharp
    {"sk_paint_set_colorfilter", sk_paint_set_colorfilter}, // SkiaSharp
    {"sk_paint_set_dither", sk_paint_set_dither}, // SkiaSharp
    {"sk_paint_set_filter_quality", sk_paint_set_filter_quality}, // SkiaSharp
    {"sk_paint_set_imagefilter", sk_paint_set_imagefilter}, // SkiaSharp
    {"sk_paint_set_maskfilter", sk_paint_set_maskfilter}, // SkiaSharp
    {"sk_paint_set_path_effect", sk_paint_set_path_effect}, // SkiaSharp
    {"sk_paint_set_shader", sk_paint_set_shader}, // SkiaSharp
    {"sk_paint_set_stroke_cap", sk_paint_set_stroke_cap}, // SkiaSharp
    {"sk_paint_set_stroke_join", sk_paint_set_stroke_join}, // SkiaSharp
    {"sk_paint_set_stroke_miter", sk_paint_set_stroke_miter}, // SkiaSharp
    {"sk_paint_set_stroke_width", sk_paint_set_stroke_width}, // SkiaSharp
    {"sk_paint_set_style", sk_paint_set_style}, // SkiaSharp
    {"sk_path_add_arc", sk_path_add_arc}, // SkiaSharp
    {"sk_path_add_circle", sk_path_add_circle}, // SkiaSharp
    {"sk_path_add_oval", sk_path_add_oval}, // SkiaSharp
    {"sk_path_add_path", sk_path_add_path}, // SkiaSharp
    {"sk_path_add_path_matrix", sk_path_add_path_matrix}, // SkiaSharp
    {"sk_path_add_path_offset", sk_path_add_path_offset}, // SkiaSharp
    {"sk_path_add_path_reverse", sk_path_add_path_reverse}, // SkiaSharp
    {"sk_path_add_poly", sk_path_add_poly}, // SkiaSharp
    {"sk_path_add_rect", sk_path_add_rect}, // SkiaSharp
    {"sk_path_add_rect_start", sk_path_add_rect_start}, // SkiaSharp
    {"sk_path_add_rounded_rect", sk_path_add_rounded_rect}, // SkiaSharp
    {"sk_path_add_rrect", sk_path_add_rrect}, // SkiaSharp
    {"sk_path_add_rrect_start", sk_path_add_rrect_start}, // SkiaSharp
    {"sk_path_arc_to", sk_path_arc_to}, // SkiaSharp
    {"sk_path_arc_to_with_oval", sk_path_arc_to_with_oval}, // SkiaSharp
    {"sk_path_arc_to_with_points", sk_path_arc_to_with_points}, // SkiaSharp
    {"sk_path_clone", sk_path_clone}, // SkiaSharp
    {"sk_path_close", sk_path_close}, // SkiaSharp
    {"sk_path_compute_tight_bounds", sk_path_compute_tight_bounds}, // SkiaSharp
    {"sk_path_conic_to", sk_path_conic_to}, // SkiaSharp
    {"sk_path_contains", sk_path_contains}, // SkiaSharp
    {"sk_path_convert_conic_to_quads", sk_path_convert_conic_to_quads}, // SkiaSharp
    {"sk_path_count_points", sk_path_count_points}, // SkiaSharp
    {"sk_path_count_verbs", sk_path_count_verbs}, // SkiaSharp
    {"sk_path_create_iter", sk_path_create_iter}, // SkiaSharp
    {"sk_path_create_rawiter", sk_path_create_rawiter}, // SkiaSharp
    {"sk_path_cubic_to", sk_path_cubic_to}, // SkiaSharp
    {"sk_path_delete", sk_path_delete}, // SkiaSharp
    {"sk_path_effect_create_1d_path", sk_path_effect_create_1d_path}, // SkiaSharp
    {"sk_path_effect_create_2d_line", sk_path_effect_create_2d_line}, // SkiaSharp
    {"sk_path_effect_create_2d_path", sk_path_effect_create_2d_path}, // SkiaSharp
    {"sk_path_effect_create_compose", sk_path_effect_create_compose}, // SkiaSharp
    {"sk_path_effect_create_corner", sk_path_effect_create_corner}, // SkiaSharp
    {"sk_path_effect_create_dash", sk_path_effect_create_dash}, // SkiaSharp
    {"sk_path_effect_create_discrete", sk_path_effect_create_discrete}, // SkiaSharp
    {"sk_path_effect_create_sum", sk_path_effect_create_sum}, // SkiaSharp
    {"sk_path_effect_create_trim", sk_path_effect_create_trim}, // SkiaSharp
    {"sk_path_effect_unref", sk_path_effect_unref}, // SkiaSharp
    {"sk_path_get_bounds", sk_path_get_bounds}, // SkiaSharp
    {"sk_path_get_filltype", sk_path_get_filltype}, // SkiaSharp
    {"sk_path_get_last_point", sk_path_get_last_point}, // SkiaSharp
    {"sk_path_get_point", sk_path_get_point}, // SkiaSharp
    {"sk_path_get_points", sk_path_get_points}, // SkiaSharp
    {"sk_path_get_segment_masks", sk_path_get_segment_masks}, // SkiaSharp
    {"sk_path_is_convex", sk_path_is_convex}, // SkiaSharp
    {"sk_path_is_line", sk_path_is_line}, // SkiaSharp
    {"sk_path_is_oval", sk_path_is_oval}, // SkiaSharp
    {"sk_path_is_rect", sk_path_is_rect}, // SkiaSharp
    {"sk_path_is_rrect", sk_path_is_rrect}, // SkiaSharp
    {"sk_path_iter_conic_weight", sk_path_iter_conic_weight}, // SkiaSharp
    {"sk_path_iter_destroy", sk_path_iter_destroy}, // SkiaSharp
    {"sk_path_iter_is_close_line", sk_path_iter_is_close_line}, // SkiaSharp
    {"sk_path_iter_is_closed_contour", sk_path_iter_is_closed_contour}, // SkiaSharp
    {"sk_path_iter_next", sk_path_iter_next}, // SkiaSharp
    {"sk_path_line_to", sk_path_line_to}, // SkiaSharp
    {"sk_path_move_to", sk_path_move_to}, // SkiaSharp
    {"sk_path_new", sk_path_new}, // SkiaSharp
    {"sk_path_parse_svg_string", sk_path_parse_svg_string}, // SkiaSharp
    {"sk_path_quad_to", sk_path_quad_to}, // SkiaSharp
    {"sk_path_rarc_to", sk_path_rarc_to}, // SkiaSharp
    {"sk_path_rawiter_conic_weight", sk_path_rawiter_conic_weight}, // SkiaSharp
    {"sk_path_rawiter_destroy", sk_path_rawiter_destroy}, // SkiaSharp
    {"sk_path_rawiter_next", sk_path_rawiter_next}, // SkiaSharp
    {"sk_path_rawiter_peek", sk_path_rawiter_peek}, // SkiaSharp
    {"sk_path_rconic_to", sk_path_rconic_to}, // SkiaSharp
    {"sk_path_rcubic_to", sk_path_rcubic_to}, // SkiaSharp
    {"sk_path_reset", sk_path_reset}, // SkiaSharp
    {"sk_path_rewind", sk_path_rewind}, // SkiaSharp
    {"sk_path_rline_to", sk_path_rline_to}, // SkiaSharp
    {"sk_path_rmove_to", sk_path_rmove_to}, // SkiaSharp
    {"sk_path_rquad_to", sk_path_rquad_to}, // SkiaSharp
    {"sk_path_set_filltype", sk_path_set_filltype}, // SkiaSharp
    {"sk_path_to_svg_string", sk_path_to_svg_string}, // SkiaSharp
    {"sk_path_transform", sk_path_transform}, // SkiaSharp
    {"sk_path_transform_to_dest", sk_path_transform_to_dest}, // SkiaSharp
    {"sk_pathmeasure_destroy", sk_pathmeasure_destroy}, // SkiaSharp
    {"sk_pathmeasure_get_length", sk_pathmeasure_get_length}, // SkiaSharp
    {"sk_pathmeasure_get_matrix", sk_pathmeasure_get_matrix}, // SkiaSharp
    {"sk_pathmeasure_get_pos_tan", sk_pathmeasure_get_pos_tan}, // SkiaSharp
    {"sk_pathmeasure_get_segment", sk_pathmeasure_get_segment}, // SkiaSharp
    {"sk_pathmeasure_is_closed", sk_pathmeasure_is_closed}, // SkiaSharp
    {"sk_pathmeasure_new", sk_pathmeasure_new}, // SkiaSharp
    {"sk_pathmeasure_new_with_path", sk_pathmeasure_new_with_path}, // SkiaSharp
    {"sk_pathmeasure_next_contour", sk_pathmeasure_next_contour}, // SkiaSharp
    {"sk_pathmeasure_set_path", sk_pathmeasure_set_path}, // SkiaSharp
    {"sk_pathop_as_winding", sk_pathop_as_winding}, // SkiaSharp
    {"sk_pathop_op", sk_pathop_op}, // SkiaSharp
    {"sk_pathop_simplify", sk_pathop_simplify}, // SkiaSharp
    {"sk_pathop_tight_bounds", sk_pathop_tight_bounds}, // SkiaSharp
    {"sk_picture_deserialize_from_data", sk_picture_deserialize_from_data}, // SkiaSharp
    {"sk_picture_deserialize_from_memory", sk_picture_deserialize_from_memory}, // SkiaSharp
    {"sk_picture_deserialize_from_stream", sk_picture_deserialize_from_stream}, // SkiaSharp
    {"sk_picture_get_cull_rect", sk_picture_get_cull_rect}, // SkiaSharp
    {"sk_picture_get_recording_canvas", sk_picture_get_recording_canvas}, // SkiaSharp
    {"sk_picture_get_unique_id", sk_picture_get_unique_id}, // SkiaSharp
    {"sk_picture_make_shader", sk_picture_make_shader}, // SkiaSharp
    {"sk_picture_recorder_begin_recording", sk_picture_recorder_begin_recording}, // SkiaSharp
    {"sk_picture_recorder_delete", sk_picture_recorder_delete}, // SkiaSharp
    {"sk_picture_recorder_end_recording", sk_picture_recorder_end_recording}, // SkiaSharp
    {"sk_picture_recorder_end_recording_as_drawable", sk_picture_recorder_end_recording_as_drawable}, // SkiaSharp
    {"sk_picture_recorder_new", sk_picture_recorder_new}, // SkiaSharp
    {"sk_picture_ref", sk_picture_ref}, // SkiaSharp
    {"sk_picture_serialize_to_data", sk_picture_serialize_to_data}, // SkiaSharp
    {"sk_picture_serialize_to_stream", sk_picture_serialize_to_stream}, // SkiaSharp
    {"sk_picture_unref", sk_picture_unref}, // SkiaSharp
    {"sk_pixmap_destructor", sk_pixmap_destructor}, // SkiaSharp
    {"sk_pixmap_encode_image", sk_pixmap_encode_image}, // SkiaSharp
    {"sk_pixmap_erase_color", sk_pixmap_erase_color}, // SkiaSharp
    {"sk_pixmap_erase_color4f", sk_pixmap_erase_color4f}, // SkiaSharp
    {"sk_pixmap_extract_subset", sk_pixmap_extract_subset}, // SkiaSharp
    {"sk_pixmap_get_info", sk_pixmap_get_info}, // SkiaSharp
    {"sk_pixmap_get_pixel_color", sk_pixmap_get_pixel_color}, // SkiaSharp
    {"sk_pixmap_get_pixels", sk_pixmap_get_pixels}, // SkiaSharp
    {"sk_pixmap_get_pixels_with_xy", sk_pixmap_get_pixels_with_xy}, // SkiaSharp
    {"sk_pixmap_get_row_bytes", sk_pixmap_get_row_bytes}, // SkiaSharp
    {"sk_pixmap_get_writable_addr", sk_pixmap_get_writable_addr}, // SkiaSharp
    {"sk_pixmap_new", sk_pixmap_new}, // SkiaSharp
    {"sk_pixmap_new_with_params", sk_pixmap_new_with_params}, // SkiaSharp
    {"sk_pixmap_read_pixels", sk_pixmap_read_pixels}, // SkiaSharp
    {"sk_pixmap_reset", sk_pixmap_reset}, // SkiaSharp
    {"sk_pixmap_reset_with_params", sk_pixmap_reset_with_params}, // SkiaSharp
    {"sk_pixmap_scale_pixels", sk_pixmap_scale_pixels}, // SkiaSharp
    {"sk_pngencoder_encode", sk_pngencoder_encode}, // SkiaSharp
    {"sk_refcnt_get_ref_count", sk_refcnt_get_ref_count}, // SkiaSharp
    {"sk_refcnt_safe_ref", sk_refcnt_safe_ref}, // SkiaSharp
    {"sk_refcnt_safe_unref", sk_refcnt_safe_unref}, // SkiaSharp
    {"sk_refcnt_unique", sk_refcnt_unique}, // SkiaSharp
    {"sk_region_cliperator_delete", sk_region_cliperator_delete}, // SkiaSharp
    {"sk_region_cliperator_done", sk_region_cliperator_done}, // SkiaSharp
    {"sk_region_cliperator_new", sk_region_cliperator_new}, // SkiaSharp
    {"sk_region_cliperator_next", sk_region_cliperator_next}, // SkiaSharp
    {"sk_region_cliperator_rect", sk_region_cliperator_rect}, // SkiaSharp
    {"sk_region_contains", sk_region_contains}, // SkiaSharp
    {"sk_region_contains_point", sk_region_contains_point}, // SkiaSharp
    {"sk_region_contains_rect", sk_region_contains_rect}, // SkiaSharp
    {"sk_region_delete", sk_region_delete}, // SkiaSharp
    {"sk_region_get_boundary_path", sk_region_get_boundary_path}, // SkiaSharp
    {"sk_region_get_bounds", sk_region_get_bounds}, // SkiaSharp
    {"sk_region_intersects", sk_region_intersects}, // SkiaSharp
    {"sk_region_intersects_rect", sk_region_intersects_rect}, // SkiaSharp
    {"sk_region_is_complex", sk_region_is_complex}, // SkiaSharp
    {"sk_region_is_empty", sk_region_is_empty}, // SkiaSharp
    {"sk_region_is_rect", sk_region_is_rect}, // SkiaSharp
    {"sk_region_iterator_delete", sk_region_iterator_delete}, // SkiaSharp
    {"sk_region_iterator_done", sk_region_iterator_done}, // SkiaSharp
    {"sk_region_iterator_new", sk_region_iterator_new}, // SkiaSharp
    {"sk_region_iterator_next", sk_region_iterator_next}, // SkiaSharp
    {"sk_region_iterator_rect", sk_region_iterator_rect}, // SkiaSharp
    {"sk_region_iterator_rewind", sk_region_iterator_rewind}, // SkiaSharp
    {"sk_region_new", sk_region_new}, // SkiaSharp
    {"sk_region_op", sk_region_op}, // SkiaSharp
    {"sk_region_op_rect", sk_region_op_rect}, // SkiaSharp
    {"sk_region_quick_contains", sk_region_quick_contains}, // SkiaSharp
    {"sk_region_quick_reject", sk_region_quick_reject}, // SkiaSharp
    {"sk_region_quick_reject_rect", sk_region_quick_reject_rect}, // SkiaSharp
    {"sk_region_set_empty", sk_region_set_empty}, // SkiaSharp
    {"sk_region_set_path", sk_region_set_path}, // SkiaSharp
    {"sk_region_set_rect", sk_region_set_rect}, // SkiaSharp
    {"sk_region_set_rects", sk_region_set_rects}, // SkiaSharp
    {"sk_region_set_region", sk_region_set_region}, // SkiaSharp
    {"sk_region_spanerator_delete", sk_region_spanerator_delete}, // SkiaSharp
    {"sk_region_spanerator_new", sk_region_spanerator_new}, // SkiaSharp
    {"sk_region_spanerator_next", sk_region_spanerator_next}, // SkiaSharp
    {"sk_region_translate", sk_region_translate}, // SkiaSharp
    {"sk_rrect_contains", sk_rrect_contains}, // SkiaSharp
    {"sk_rrect_delete", sk_rrect_delete}, // SkiaSharp
    {"sk_rrect_get_height", sk_rrect_get_height}, // SkiaSharp
    {"sk_rrect_get_radii", sk_rrect_get_radii}, // SkiaSharp
    {"sk_rrect_get_rect", sk_rrect_get_rect}, // SkiaSharp
    {"sk_rrect_get_type", sk_rrect_get_type}, // SkiaSharp
    {"sk_rrect_get_width", sk_rrect_get_width}, // SkiaSharp
    {"sk_rrect_inset", sk_rrect_inset}, // SkiaSharp
    {"sk_rrect_is_valid", sk_rrect_is_valid}, // SkiaSharp
    {"sk_rrect_new", sk_rrect_new}, // SkiaSharp
    {"sk_rrect_new_copy", sk_rrect_new_copy}, // SkiaSharp
    {"sk_rrect_offset", sk_rrect_offset}, // SkiaSharp
    {"sk_rrect_outset", sk_rrect_outset}, // SkiaSharp
    {"sk_rrect_set_empty", sk_rrect_set_empty}, // SkiaSharp
    {"sk_rrect_set_nine_patch", sk_rrect_set_nine_patch}, // SkiaSharp
    {"sk_rrect_set_oval", sk_rrect_set_oval}, // SkiaSharp
    {"sk_rrect_set_rect", sk_rrect_set_rect}, // SkiaSharp
    {"sk_rrect_set_rect_radii", sk_rrect_set_rect_radii}, // SkiaSharp
    {"sk_rrect_set_rect_xy", sk_rrect_set_rect_xy}, // SkiaSharp
    {"sk_rrect_transform", sk_rrect_transform}, // SkiaSharp
    {"sk_runtimeeffect_get_child_name", sk_runtimeeffect_get_child_name}, // SkiaSharp
    {"sk_runtimeeffect_get_children_count", sk_runtimeeffect_get_children_count}, // SkiaSharp
    {"sk_runtimeeffect_get_uniform_from_index", sk_runtimeeffect_get_uniform_from_index}, // SkiaSharp
    {"sk_runtimeeffect_get_uniform_from_name", sk_runtimeeffect_get_uniform_from_name}, // SkiaSharp
    {"sk_runtimeeffect_get_uniform_name", sk_runtimeeffect_get_uniform_name}, // SkiaSharp
    {"sk_runtimeeffect_get_uniform_size", sk_runtimeeffect_get_uniform_size}, // SkiaSharp
    {"sk_runtimeeffect_get_uniforms_count", sk_runtimeeffect_get_uniforms_count}, // SkiaSharp
    {"sk_runtimeeffect_make", sk_runtimeeffect_make}, // SkiaSharp
    {"sk_runtimeeffect_make_color_filter", sk_runtimeeffect_make_color_filter}, // SkiaSharp
    {"sk_runtimeeffect_make_shader", sk_runtimeeffect_make_shader}, // SkiaSharp
    {"sk_runtimeeffect_uniform_get_offset", sk_runtimeeffect_uniform_get_offset}, // SkiaSharp
    {"sk_runtimeeffect_uniform_get_size_in_bytes", sk_runtimeeffect_uniform_get_size_in_bytes}, // SkiaSharp
    {"sk_runtimeeffect_unref", sk_runtimeeffect_unref}, // SkiaSharp
    {"sk_shader_new_blend", sk_shader_new_blend}, // SkiaSharp
    {"sk_shader_new_color", sk_shader_new_color}, // SkiaSharp
    {"sk_shader_new_color4f", sk_shader_new_color4f}, // SkiaSharp
    {"sk_shader_new_empty", sk_shader_new_empty}, // SkiaSharp
    {"sk_shader_new_lerp", sk_shader_new_lerp}, // SkiaSharp
    {"sk_shader_new_linear_gradient", sk_shader_new_linear_gradient}, // SkiaSharp
    {"sk_shader_new_linear_gradient_color4f", sk_shader_new_linear_gradient_color4f}, // SkiaSharp
    {"sk_shader_new_perlin_noise_fractal_noise", sk_shader_new_perlin_noise_fractal_noise}, // SkiaSharp
    {"sk_shader_new_perlin_noise_improved_noise", sk_shader_new_perlin_noise_improved_noise}, // SkiaSharp
    {"sk_shader_new_perlin_noise_turbulence", sk_shader_new_perlin_noise_turbulence}, // SkiaSharp
    {"sk_shader_new_radial_gradient", sk_shader_new_radial_gradient}, // SkiaSharp
    {"sk_shader_new_radial_gradient_color4f", sk_shader_new_radial_gradient_color4f}, // SkiaSharp
    {"sk_shader_new_sweep_gradient", sk_shader_new_sweep_gradient}, // SkiaSharp
    {"sk_shader_new_sweep_gradient_color4f", sk_shader_new_sweep_gradient_color4f}, // SkiaSharp
    {"sk_shader_new_two_point_conical_gradient", sk_shader_new_two_point_conical_gradient}, // SkiaSharp
    {"sk_shader_new_two_point_conical_gradient_color4f", sk_shader_new_two_point_conical_gradient_color4f}, // SkiaSharp
    {"sk_shader_ref", sk_shader_ref}, // SkiaSharp
    {"sk_shader_unref", sk_shader_unref}, // SkiaSharp
    {"sk_shader_with_color_filter", sk_shader_with_color_filter}, // SkiaSharp
    {"sk_shader_with_local_matrix", sk_shader_with_local_matrix}, // SkiaSharp
    {"sk_stream_asset_destroy", sk_stream_asset_destroy}, // SkiaSharp
    {"sk_stream_destroy", sk_stream_destroy}, // SkiaSharp
    {"sk_stream_duplicate", sk_stream_duplicate}, // SkiaSharp
    {"sk_stream_fork", sk_stream_fork}, // SkiaSharp
    {"sk_stream_get_length", sk_stream_get_length}, // SkiaSharp
    {"sk_stream_get_memory_base", sk_stream_get_memory_base}, // SkiaSharp
    {"sk_stream_get_position", sk_stream_get_position}, // SkiaSharp
    {"sk_stream_has_length", sk_stream_has_length}, // SkiaSharp
    {"sk_stream_has_position", sk_stream_has_position}, // SkiaSharp
    {"sk_stream_is_at_end", sk_stream_is_at_end}, // SkiaSharp
    {"sk_stream_move", sk_stream_move}, // SkiaSharp
    {"sk_stream_peek", sk_stream_peek}, // SkiaSharp
    {"sk_stream_read", sk_stream_read}, // SkiaSharp
    {"sk_stream_read_bool", sk_stream_read_bool}, // SkiaSharp
    {"sk_stream_read_s16", sk_stream_read_s16}, // SkiaSharp
    {"sk_stream_read_s32", sk_stream_read_s32}, // SkiaSharp
    {"sk_stream_read_s8", sk_stream_read_s8}, // SkiaSharp
    {"sk_stream_read_u16", sk_stream_read_u16}, // SkiaSharp
    {"sk_stream_read_u32", sk_stream_read_u32}, // SkiaSharp
    {"sk_stream_read_u8", sk_stream_read_u8}, // SkiaSharp
    {"sk_stream_rewind", sk_stream_rewind}, // SkiaSharp
    {"sk_stream_seek", sk_stream_seek}, // SkiaSharp
    {"sk_stream_skip", sk_stream_skip}, // SkiaSharp
    {"sk_string_destructor", sk_string_destructor}, // SkiaSharp
    {"sk_string_get_c_str", sk_string_get_c_str}, // SkiaSharp
    {"sk_string_get_size", sk_string_get_size}, // SkiaSharp
    {"sk_string_new_empty", sk_string_new_empty}, // SkiaSharp
    {"sk_string_new_with_copy", sk_string_new_with_copy}, // SkiaSharp
    {"sk_surface_draw", sk_surface_draw}, // SkiaSharp
    {"sk_surface_flush", sk_surface_flush}, // SkiaSharp
    {"sk_surface_flush_and_submit", sk_surface_flush_and_submit}, // SkiaSharp
    {"sk_surface_get_canvas", sk_surface_get_canvas}, // SkiaSharp
    {"sk_surface_get_props", sk_surface_get_props}, // SkiaSharp
    {"sk_surface_get_recording_context", sk_surface_get_recording_context}, // SkiaSharp
    {"sk_surface_new_backend_render_target", sk_surface_new_backend_render_target}, // SkiaSharp
    {"sk_surface_new_backend_texture", sk_surface_new_backend_texture}, // SkiaSharp
    {"sk_surface_new_image_snapshot", sk_surface_new_image_snapshot}, // SkiaSharp
    {"sk_surface_new_image_snapshot_with_crop", sk_surface_new_image_snapshot_with_crop}, // SkiaSharp
    {"sk_surface_new_metal_layer", sk_surface_new_metal_layer}, // SkiaSharp
    {"sk_surface_new_metal_view", sk_surface_new_metal_view}, // SkiaSharp
    {"sk_surface_new_null", sk_surface_new_null}, // SkiaSharp
    {"sk_surface_new_raster", sk_surface_new_raster}, // SkiaSharp
    {"sk_surface_new_raster_direct", sk_surface_new_raster_direct}, // SkiaSharp
    {"sk_surface_new_render_target", sk_surface_new_render_target}, // SkiaSharp
    {"sk_surface_peek_pixels", sk_surface_peek_pixels}, // SkiaSharp
    {"sk_surface_read_pixels", sk_surface_read_pixels}, // SkiaSharp
    {"sk_surface_unref", sk_surface_unref}, // SkiaSharp
    {"sk_surfaceprops_delete", sk_surfaceprops_delete}, // SkiaSharp
    {"sk_surfaceprops_get_flags", sk_surfaceprops_get_flags}, // SkiaSharp
    {"sk_surfaceprops_get_pixel_geometry", sk_surfaceprops_get_pixel_geometry}, // SkiaSharp
    {"sk_surfaceprops_new", sk_surfaceprops_new}, // SkiaSharp
    {"sk_svgcanvas_create_with_stream", sk_svgcanvas_create_with_stream}, // SkiaSharp
    {"sk_svgcanvas_create_with_writer", sk_svgcanvas_create_with_writer}, // SkiaSharp
    {"sk_swizzle_swap_rb", sk_swizzle_swap_rb}, // SkiaSharp
    {"sk_text_utils_get_path", sk_text_utils_get_path}, // SkiaSharp
    {"sk_text_utils_get_pos_path", sk_text_utils_get_pos_path}, // SkiaSharp
    {"sk_textblob_builder_alloc_run", sk_textblob_builder_alloc_run}, // SkiaSharp
    {"sk_textblob_builder_alloc_run_pos", sk_textblob_builder_alloc_run_pos}, // SkiaSharp
    {"sk_textblob_builder_alloc_run_pos_h", sk_textblob_builder_alloc_run_pos_h}, // SkiaSharp
    {"sk_textblob_builder_alloc_run_rsxform", sk_textblob_builder_alloc_run_rsxform}, // SkiaSharp
    {"sk_textblob_builder_alloc_run_text", sk_textblob_builder_alloc_run_text}, // SkiaSharp
    {"sk_textblob_builder_alloc_run_text_pos", sk_textblob_builder_alloc_run_text_pos}, // SkiaSharp
    {"sk_textblob_builder_alloc_run_text_pos_h", sk_textblob_builder_alloc_run_text_pos_h}, // SkiaSharp
    {"sk_textblob_builder_delete", sk_textblob_builder_delete}, // SkiaSharp
    {"sk_textblob_builder_make", sk_textblob_builder_make}, // SkiaSharp
    {"sk_textblob_builder_new", sk_textblob_builder_new}, // SkiaSharp
    {"sk_textblob_get_bounds", sk_textblob_get_bounds}, // SkiaSharp
    {"sk_textblob_get_intercepts", sk_textblob_get_intercepts}, // SkiaSharp
    {"sk_textblob_get_unique_id", sk_textblob_get_unique_id}, // SkiaSharp
    {"sk_textblob_ref", sk_textblob_ref}, // SkiaSharp
    {"sk_textblob_unref", sk_textblob_unref}, // SkiaSharp
    {"sk_typeface_copy_table_data", sk_typeface_copy_table_data}, // SkiaSharp
    {"sk_typeface_count_glyphs", sk_typeface_count_glyphs}, // SkiaSharp
    {"sk_typeface_count_tables", sk_typeface_count_tables}, // SkiaSharp
    {"sk_typeface_create_default", sk_typeface_create_default}, // SkiaSharp
    {"sk_typeface_create_from_data", sk_typeface_create_from_data}, // SkiaSharp
    {"sk_typeface_create_from_file", sk_typeface_create_from_file}, // SkiaSharp
    {"sk_typeface_create_from_name", sk_typeface_create_from_name}, // SkiaSharp
    {"sk_typeface_create_from_stream", sk_typeface_create_from_stream}, // SkiaSharp
    {"sk_typeface_get_family_name", sk_typeface_get_family_name}, // SkiaSharp
    {"sk_typeface_get_font_slant", sk_typeface_get_font_slant}, // SkiaSharp
    {"sk_typeface_get_font_weight", sk_typeface_get_font_weight}, // SkiaSharp
    {"sk_typeface_get_font_width", sk_typeface_get_font_width}, // SkiaSharp
    {"sk_typeface_get_fontstyle", sk_typeface_get_fontstyle}, // SkiaSharp
    {"sk_typeface_get_kerning_pair_adjustments", sk_typeface_get_kerning_pair_adjustments}, // SkiaSharp
    {"sk_typeface_get_table_data", sk_typeface_get_table_data}, // SkiaSharp
    {"sk_typeface_get_table_size", sk_typeface_get_table_size}, // SkiaSharp
    {"sk_typeface_get_table_tags", sk_typeface_get_table_tags}, // SkiaSharp
    {"sk_typeface_get_units_per_em", sk_typeface_get_units_per_em}, // SkiaSharp
    {"sk_typeface_is_fixed_pitch", sk_typeface_is_fixed_pitch}, // SkiaSharp
    {"sk_typeface_open_stream", sk_typeface_open_stream}, // SkiaSharp
    {"sk_typeface_ref_default", sk_typeface_ref_default}, // SkiaSharp
    {"sk_typeface_unichar_to_glyph", sk_typeface_unichar_to_glyph}, // SkiaSharp
    {"sk_typeface_unichars_to_glyphs", sk_typeface_unichars_to_glyphs}, // SkiaSharp
    {"sk_typeface_unref", sk_typeface_unref}, // SkiaSharp
    {"sk_version_get_increment", sk_version_get_increment}, // SkiaSharp
    {"sk_version_get_milestone", sk_version_get_milestone}, // SkiaSharp
    {"sk_version_get_string", sk_version_get_string}, // SkiaSharp
    {"sk_vertices_make_copy", sk_vertices_make_copy}, // SkiaSharp
    {"sk_vertices_ref", sk_vertices_ref}, // SkiaSharp
    {"sk_vertices_unref", sk_vertices_unref}, // SkiaSharp
    {"sk_webpencoder_encode", sk_webpencoder_encode}, // SkiaSharp
    {"sk_wstream_bytes_written", sk_wstream_bytes_written}, // SkiaSharp
    {"sk_wstream_flush", sk_wstream_flush}, // SkiaSharp
    {"sk_wstream_get_size_of_packed_uint", sk_wstream_get_size_of_packed_uint}, // SkiaSharp
    {"sk_wstream_newline", sk_wstream_newline}, // SkiaSharp
    {"sk_wstream_write", sk_wstream_write}, // SkiaSharp
    {"sk_wstream_write_16", sk_wstream_write_16}, // SkiaSharp
    {"sk_wstream_write_32", sk_wstream_write_32}, // SkiaSharp
    {"sk_wstream_write_8", sk_wstream_write_8}, // SkiaSharp
    {"sk_wstream_write_bigdec_as_text", sk_wstream_write_bigdec_as_text}, // SkiaSharp
    {"sk_wstream_write_bool", sk_wstream_write_bool}, // SkiaSharp
    {"sk_wstream_write_dec_as_text", sk_wstream_write_dec_as_text}, // SkiaSharp
    {"sk_wstream_write_hex_as_text", sk_wstream_write_hex_as_text}, // SkiaSharp
    {"sk_wstream_write_packed_uint", sk_wstream_write_packed_uint}, // SkiaSharp
    {"sk_wstream_write_scalar", sk_wstream_write_scalar}, // SkiaSharp
    {"sk_wstream_write_scalar_as_text", sk_wstream_write_scalar_as_text}, // SkiaSharp
    {"sk_wstream_write_stream", sk_wstream_write_stream}, // SkiaSharp
    {"sk_wstream_write_text", sk_wstream_write_text}, // SkiaSharp
    {"sk_xmlstreamwriter_delete", sk_xmlstreamwriter_delete}, // SkiaSharp
    {"sk_xmlstreamwriter_new", sk_xmlstreamwriter_new}, // SkiaSharp
    {NULL, NULL}
};
static PinvokeImport libHarfBuzzSharp_imports [] = {
    {"hb_blob_copy_writable_or_fail", hb_blob_copy_writable_or_fail}, // HarfBuzzSharp
    {"hb_blob_create", hb_blob_create}, // HarfBuzzSharp
    {"hb_blob_create_from_file", hb_blob_create_from_file}, // HarfBuzzSharp
    {"hb_blob_create_from_file_or_fail", hb_blob_create_from_file_or_fail}, // HarfBuzzSharp
    {"hb_blob_create_or_fail", hb_blob_create_or_fail}, // HarfBuzzSharp
    {"hb_blob_create_sub_blob", hb_blob_create_sub_blob}, // HarfBuzzSharp
    {"hb_blob_destroy", hb_blob_destroy}, // HarfBuzzSharp
    {"hb_blob_get_data", hb_blob_get_data}, // HarfBuzzSharp
    {"hb_blob_get_data_writable", hb_blob_get_data_writable}, // HarfBuzzSharp
    {"hb_blob_get_empty", hb_blob_get_empty}, // HarfBuzzSharp
    {"hb_blob_get_length", hb_blob_get_length}, // HarfBuzzSharp
    {"hb_blob_is_immutable", hb_blob_is_immutable}, // HarfBuzzSharp
    {"hb_blob_make_immutable", hb_blob_make_immutable}, // HarfBuzzSharp
    {"hb_blob_reference", hb_blob_reference}, // HarfBuzzSharp
    {"hb_buffer_add", hb_buffer_add}, // HarfBuzzSharp
    {"hb_buffer_add_codepoints", hb_buffer_add_codepoints}, // HarfBuzzSharp
    {"hb_buffer_add_latin1", hb_buffer_add_latin1}, // HarfBuzzSharp
    {"hb_buffer_add_utf16", hb_buffer_add_utf16}, // HarfBuzzSharp
    {"hb_buffer_add_utf32", hb_buffer_add_utf32}, // HarfBuzzSharp
    {"hb_buffer_add_utf8", hb_buffer_add_utf8}, // HarfBuzzSharp
    {"hb_buffer_allocation_successful", hb_buffer_allocation_successful}, // HarfBuzzSharp
    {"hb_buffer_append", hb_buffer_append}, // HarfBuzzSharp
    {"hb_buffer_clear_contents", hb_buffer_clear_contents}, // HarfBuzzSharp
    {"hb_buffer_create", hb_buffer_create}, // HarfBuzzSharp
    {"hb_buffer_deserialize_glyphs", hb_buffer_deserialize_glyphs}, // HarfBuzzSharp
    {"hb_buffer_deserialize_unicode", hb_buffer_deserialize_unicode}, // HarfBuzzSharp
    {"hb_buffer_destroy", hb_buffer_destroy}, // HarfBuzzSharp
    {"hb_buffer_diff", hb_buffer_diff}, // HarfBuzzSharp
    {"hb_buffer_get_cluster_level", hb_buffer_get_cluster_level}, // HarfBuzzSharp
    {"hb_buffer_get_content_type", hb_buffer_get_content_type}, // HarfBuzzSharp
    {"hb_buffer_get_direction", hb_buffer_get_direction}, // HarfBuzzSharp
    {"hb_buffer_get_empty", hb_buffer_get_empty}, // HarfBuzzSharp
    {"hb_buffer_get_flags", hb_buffer_get_flags}, // HarfBuzzSharp
    {"hb_buffer_get_glyph_infos", hb_buffer_get_glyph_infos}, // HarfBuzzSharp
    {"hb_buffer_get_glyph_positions", hb_buffer_get_glyph_positions}, // HarfBuzzSharp
    {"hb_buffer_get_invisible_glyph", hb_buffer_get_invisible_glyph}, // HarfBuzzSharp
    {"hb_buffer_get_language", hb_buffer_get_language}, // HarfBuzzSharp
    {"hb_buffer_get_length", hb_buffer_get_length}, // HarfBuzzSharp
    {"hb_buffer_get_replacement_codepoint", hb_buffer_get_replacement_codepoint}, // HarfBuzzSharp
    {"hb_buffer_get_script", hb_buffer_get_script}, // HarfBuzzSharp
    {"hb_buffer_get_unicode_funcs", hb_buffer_get_unicode_funcs}, // HarfBuzzSharp
    {"hb_buffer_guess_segment_properties", hb_buffer_guess_segment_properties}, // HarfBuzzSharp
    {"hb_buffer_has_positions", hb_buffer_has_positions}, // HarfBuzzSharp
    {"hb_buffer_normalize_glyphs", hb_buffer_normalize_glyphs}, // HarfBuzzSharp
    {"hb_buffer_pre_allocate", hb_buffer_pre_allocate}, // HarfBuzzSharp
    {"hb_buffer_reference", hb_buffer_reference}, // HarfBuzzSharp
    {"hb_buffer_reset", hb_buffer_reset}, // HarfBuzzSharp
    {"hb_buffer_reverse", hb_buffer_reverse}, // HarfBuzzSharp
    {"hb_buffer_reverse_clusters", hb_buffer_reverse_clusters}, // HarfBuzzSharp
    {"hb_buffer_reverse_range", hb_buffer_reverse_range}, // HarfBuzzSharp
    {"hb_buffer_serialize", hb_buffer_serialize}, // HarfBuzzSharp
    {"hb_buffer_serialize_format_from_string", hb_buffer_serialize_format_from_string}, // HarfBuzzSharp
    {"hb_buffer_serialize_format_to_string", hb_buffer_serialize_format_to_string}, // HarfBuzzSharp
    {"hb_buffer_serialize_glyphs", hb_buffer_serialize_glyphs}, // HarfBuzzSharp
    {"hb_buffer_serialize_list_formats", hb_buffer_serialize_list_formats}, // HarfBuzzSharp
    {"hb_buffer_serialize_unicode", hb_buffer_serialize_unicode}, // HarfBuzzSharp
    {"hb_buffer_set_cluster_level", hb_buffer_set_cluster_level}, // HarfBuzzSharp
    {"hb_buffer_set_content_type", hb_buffer_set_content_type}, // HarfBuzzSharp
    {"hb_buffer_set_direction", hb_buffer_set_direction}, // HarfBuzzSharp
    {"hb_buffer_set_flags", hb_buffer_set_flags}, // HarfBuzzSharp
    {"hb_buffer_set_invisible_glyph", hb_buffer_set_invisible_glyph}, // HarfBuzzSharp
    {"hb_buffer_set_language", hb_buffer_set_language}, // HarfBuzzSharp
    {"hb_buffer_set_length", hb_buffer_set_length}, // HarfBuzzSharp
    {"hb_buffer_set_message_func", hb_buffer_set_message_func}, // HarfBuzzSharp
    {"hb_buffer_set_replacement_codepoint", hb_buffer_set_replacement_codepoint}, // HarfBuzzSharp
    {"hb_buffer_set_script", hb_buffer_set_script}, // HarfBuzzSharp
    {"hb_buffer_set_unicode_funcs", hb_buffer_set_unicode_funcs}, // HarfBuzzSharp
    {"hb_color_get_alpha", hb_color_get_alpha}, // HarfBuzzSharp
    {"hb_color_get_blue", hb_color_get_blue}, // HarfBuzzSharp
    {"hb_color_get_green", hb_color_get_green}, // HarfBuzzSharp
    {"hb_color_get_red", hb_color_get_red}, // HarfBuzzSharp
    {"hb_direction_from_string", hb_direction_from_string}, // HarfBuzzSharp
    {"hb_direction_to_string", hb_direction_to_string}, // HarfBuzzSharp
    {"hb_face_builder_add_table", hb_face_builder_add_table}, // HarfBuzzSharp
    {"hb_face_builder_create", hb_face_builder_create}, // HarfBuzzSharp
    {"hb_face_collect_unicodes", hb_face_collect_unicodes}, // HarfBuzzSharp
    {"hb_face_collect_variation_selectors", hb_face_collect_variation_selectors}, // HarfBuzzSharp
    {"hb_face_collect_variation_unicodes", hb_face_collect_variation_unicodes}, // HarfBuzzSharp
    {"hb_face_count", hb_face_count}, // HarfBuzzSharp
    {"hb_face_create", hb_face_create}, // HarfBuzzSharp
    {"hb_face_create_for_tables", hb_face_create_for_tables}, // HarfBuzzSharp
    {"hb_face_destroy", hb_face_destroy}, // HarfBuzzSharp
    {"hb_face_get_empty", hb_face_get_empty}, // HarfBuzzSharp
    {"hb_face_get_glyph_count", hb_face_get_glyph_count}, // HarfBuzzSharp
    {"hb_face_get_index", hb_face_get_index}, // HarfBuzzSharp
    {"hb_face_get_table_tags", hb_face_get_table_tags}, // HarfBuzzSharp
    {"hb_face_get_upem", hb_face_get_upem}, // HarfBuzzSharp
    {"hb_face_is_immutable", hb_face_is_immutable}, // HarfBuzzSharp
    {"hb_face_make_immutable", hb_face_make_immutable}, // HarfBuzzSharp
    {"hb_face_reference", hb_face_reference}, // HarfBuzzSharp
    {"hb_face_reference_blob", hb_face_reference_blob}, // HarfBuzzSharp
    {"hb_face_reference_table", hb_face_reference_table}, // HarfBuzzSharp
    {"hb_face_set_glyph_count", hb_face_set_glyph_count}, // HarfBuzzSharp
    {"hb_face_set_index", hb_face_set_index}, // HarfBuzzSharp
    {"hb_face_set_upem", hb_face_set_upem}, // HarfBuzzSharp
    {"hb_feature_from_string", hb_feature_from_string}, // HarfBuzzSharp
    {"hb_feature_to_string", hb_feature_to_string}, // HarfBuzzSharp
    {"hb_font_add_glyph_origin_for_direction", hb_font_add_glyph_origin_for_direction}, // HarfBuzzSharp
    {"hb_font_create", hb_font_create}, // HarfBuzzSharp
    {"hb_font_create_sub_font", hb_font_create_sub_font}, // HarfBuzzSharp
    {"hb_font_destroy", hb_font_destroy}, // HarfBuzzSharp
    {"hb_font_funcs_create", hb_font_funcs_create}, // HarfBuzzSharp
    {"hb_font_funcs_destroy", hb_font_funcs_destroy}, // HarfBuzzSharp
    {"hb_font_funcs_get_empty", hb_font_funcs_get_empty}, // HarfBuzzSharp
    {"hb_font_funcs_is_immutable", hb_font_funcs_is_immutable}, // HarfBuzzSharp
    {"hb_font_funcs_make_immutable", hb_font_funcs_make_immutable}, // HarfBuzzSharp
    {"hb_font_funcs_reference", hb_font_funcs_reference}, // HarfBuzzSharp
    {"hb_font_funcs_set_font_h_extents_func", hb_font_funcs_set_font_h_extents_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_font_v_extents_func", hb_font_funcs_set_font_v_extents_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_contour_point_func", hb_font_funcs_set_glyph_contour_point_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_extents_func", hb_font_funcs_set_glyph_extents_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_from_name_func", hb_font_funcs_set_glyph_from_name_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_h_advance_func", hb_font_funcs_set_glyph_h_advance_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_h_advances_func", hb_font_funcs_set_glyph_h_advances_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_h_kerning_func", hb_font_funcs_set_glyph_h_kerning_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_h_origin_func", hb_font_funcs_set_glyph_h_origin_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_name_func", hb_font_funcs_set_glyph_name_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_v_advance_func", hb_font_funcs_set_glyph_v_advance_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_v_advances_func", hb_font_funcs_set_glyph_v_advances_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_glyph_v_origin_func", hb_font_funcs_set_glyph_v_origin_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_nominal_glyph_func", hb_font_funcs_set_nominal_glyph_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_nominal_glyphs_func", hb_font_funcs_set_nominal_glyphs_func}, // HarfBuzzSharp
    {"hb_font_funcs_set_variation_glyph_func", hb_font_funcs_set_variation_glyph_func}, // HarfBuzzSharp
    {"hb_font_get_empty", hb_font_get_empty}, // HarfBuzzSharp
    {"hb_font_get_extents_for_direction", hb_font_get_extents_for_direction}, // HarfBuzzSharp
    {"hb_font_get_face", hb_font_get_face}, // HarfBuzzSharp
    {"hb_font_get_glyph", hb_font_get_glyph}, // HarfBuzzSharp
    {"hb_font_get_glyph_advance_for_direction", hb_font_get_glyph_advance_for_direction}, // HarfBuzzSharp
    {"hb_font_get_glyph_advances_for_direction", hb_font_get_glyph_advances_for_direction}, // HarfBuzzSharp
    {"hb_font_get_glyph_contour_point", hb_font_get_glyph_contour_point}, // HarfBuzzSharp
    {"hb_font_get_glyph_contour_point_for_origin", hb_font_get_glyph_contour_point_for_origin}, // HarfBuzzSharp
    {"hb_font_get_glyph_extents", hb_font_get_glyph_extents}, // HarfBuzzSharp
    {"hb_font_get_glyph_extents_for_origin", hb_font_get_glyph_extents_for_origin}, // HarfBuzzSharp
    {"hb_font_get_glyph_from_name", hb_font_get_glyph_from_name}, // HarfBuzzSharp
    {"hb_font_get_glyph_h_advance", hb_font_get_glyph_h_advance}, // HarfBuzzSharp
    {"hb_font_get_glyph_h_advances", hb_font_get_glyph_h_advances}, // HarfBuzzSharp
    {"hb_font_get_glyph_h_kerning", hb_font_get_glyph_h_kerning}, // HarfBuzzSharp
    {"hb_font_get_glyph_h_origin", hb_font_get_glyph_h_origin}, // HarfBuzzSharp
    {"hb_font_get_glyph_kerning_for_direction", hb_font_get_glyph_kerning_for_direction}, // HarfBuzzSharp
    {"hb_font_get_glyph_name", hb_font_get_glyph_name}, // HarfBuzzSharp
    {"hb_font_get_glyph_origin_for_direction", hb_font_get_glyph_origin_for_direction}, // HarfBuzzSharp
    {"hb_font_get_glyph_v_advance", hb_font_get_glyph_v_advance}, // HarfBuzzSharp
    {"hb_font_get_glyph_v_advances", hb_font_get_glyph_v_advances}, // HarfBuzzSharp
    {"hb_font_get_glyph_v_origin", hb_font_get_glyph_v_origin}, // HarfBuzzSharp
    {"hb_font_get_h_extents", hb_font_get_h_extents}, // HarfBuzzSharp
    {"hb_font_get_nominal_glyph", hb_font_get_nominal_glyph}, // HarfBuzzSharp
    {"hb_font_get_nominal_glyphs", hb_font_get_nominal_glyphs}, // HarfBuzzSharp
    {"hb_font_get_parent", hb_font_get_parent}, // HarfBuzzSharp
    {"hb_font_get_ppem", hb_font_get_ppem}, // HarfBuzzSharp
    {"hb_font_get_ptem", hb_font_get_ptem}, // HarfBuzzSharp
    {"hb_font_get_scale", hb_font_get_scale}, // HarfBuzzSharp
    {"hb_font_get_v_extents", hb_font_get_v_extents}, // HarfBuzzSharp
    {"hb_font_get_var_coords_normalized", hb_font_get_var_coords_normalized}, // HarfBuzzSharp
    {"hb_font_get_variation_glyph", hb_font_get_variation_glyph}, // HarfBuzzSharp
    {"hb_font_glyph_from_string", hb_font_glyph_from_string}, // HarfBuzzSharp
    {"hb_font_glyph_to_string", hb_font_glyph_to_string}, // HarfBuzzSharp
    {"hb_font_is_immutable", hb_font_is_immutable}, // HarfBuzzSharp
    {"hb_font_make_immutable", hb_font_make_immutable}, // HarfBuzzSharp
    {"hb_font_reference", hb_font_reference}, // HarfBuzzSharp
    {"hb_font_set_face", hb_font_set_face}, // HarfBuzzSharp
    {"hb_font_set_funcs", hb_font_set_funcs}, // HarfBuzzSharp
    {"hb_font_set_funcs_data", hb_font_set_funcs_data}, // HarfBuzzSharp
    {"hb_font_set_parent", hb_font_set_parent}, // HarfBuzzSharp
    {"hb_font_set_ppem", hb_font_set_ppem}, // HarfBuzzSharp
    {"hb_font_set_ptem", hb_font_set_ptem}, // HarfBuzzSharp
    {"hb_font_set_scale", hb_font_set_scale}, // HarfBuzzSharp
    {"hb_font_set_var_coords_design", hb_font_set_var_coords_design}, // HarfBuzzSharp
    {"hb_font_set_var_coords_normalized", hb_font_set_var_coords_normalized}, // HarfBuzzSharp
    {"hb_font_set_var_named_instance", hb_font_set_var_named_instance}, // HarfBuzzSharp
    {"hb_font_set_variations", hb_font_set_variations}, // HarfBuzzSharp
    {"hb_font_subtract_glyph_origin_for_direction", hb_font_subtract_glyph_origin_for_direction}, // HarfBuzzSharp
    {"hb_glyph_info_get_glyph_flags", hb_glyph_info_get_glyph_flags}, // HarfBuzzSharp
    {"hb_language_from_string", hb_language_from_string}, // HarfBuzzSharp
    {"hb_language_get_default", hb_language_get_default}, // HarfBuzzSharp
    {"hb_language_to_string", hb_language_to_string}, // HarfBuzzSharp
    {"hb_map_allocation_successful", hb_map_allocation_successful}, // HarfBuzzSharp
    {"hb_map_clear", hb_map_clear}, // HarfBuzzSharp
    {"hb_map_create", hb_map_create}, // HarfBuzzSharp
    {"hb_map_del", hb_map_del}, // HarfBuzzSharp
    {"hb_map_destroy", hb_map_destroy}, // HarfBuzzSharp
    {"hb_map_get", hb_map_get}, // HarfBuzzSharp
    {"hb_map_get_empty", hb_map_get_empty}, // HarfBuzzSharp
    {"hb_map_get_population", hb_map_get_population}, // HarfBuzzSharp
    {"hb_map_has", hb_map_has}, // HarfBuzzSharp
    {"hb_map_is_empty", hb_map_is_empty}, // HarfBuzzSharp
    {"hb_map_reference", hb_map_reference}, // HarfBuzzSharp
    {"hb_map_set", hb_map_set}, // HarfBuzzSharp
    {"hb_ot_color_glyph_get_layers", hb_ot_color_glyph_get_layers}, // HarfBuzzSharp
    {"hb_ot_color_glyph_reference_png", hb_ot_color_glyph_reference_png}, // HarfBuzzSharp
    {"hb_ot_color_glyph_reference_svg", hb_ot_color_glyph_reference_svg}, // HarfBuzzSharp
    {"hb_ot_color_has_layers", hb_ot_color_has_layers}, // HarfBuzzSharp
    {"hb_ot_color_has_palettes", hb_ot_color_has_palettes}, // HarfBuzzSharp
    {"hb_ot_color_has_png", hb_ot_color_has_png}, // HarfBuzzSharp
    {"hb_ot_color_has_svg", hb_ot_color_has_svg}, // HarfBuzzSharp
    {"hb_ot_color_palette_color_get_name_id", hb_ot_color_palette_color_get_name_id}, // HarfBuzzSharp
    {"hb_ot_color_palette_get_colors", hb_ot_color_palette_get_colors}, // HarfBuzzSharp
    {"hb_ot_color_palette_get_count", hb_ot_color_palette_get_count}, // HarfBuzzSharp
    {"hb_ot_color_palette_get_flags", hb_ot_color_palette_get_flags}, // HarfBuzzSharp
    {"hb_ot_color_palette_get_name_id", hb_ot_color_palette_get_name_id}, // HarfBuzzSharp
    {"hb_ot_font_set_funcs", hb_ot_font_set_funcs}, // HarfBuzzSharp
    {"hb_ot_layout_collect_features", hb_ot_layout_collect_features}, // HarfBuzzSharp
    {"hb_ot_layout_collect_lookups", hb_ot_layout_collect_lookups}, // HarfBuzzSharp
    {"hb_ot_layout_feature_get_characters", hb_ot_layout_feature_get_characters}, // HarfBuzzSharp
    {"hb_ot_layout_feature_get_lookups", hb_ot_layout_feature_get_lookups}, // HarfBuzzSharp
    {"hb_ot_layout_feature_get_name_ids", hb_ot_layout_feature_get_name_ids}, // HarfBuzzSharp
    {"hb_ot_layout_feature_with_variations_get_lookups", hb_ot_layout_feature_with_variations_get_lookups}, // HarfBuzzSharp
    {"hb_ot_layout_get_attach_points", hb_ot_layout_get_attach_points}, // HarfBuzzSharp
    {"hb_ot_layout_get_baseline", hb_ot_layout_get_baseline}, // HarfBuzzSharp
    {"hb_ot_layout_get_glyph_class", hb_ot_layout_get_glyph_class}, // HarfBuzzSharp
    {"hb_ot_layout_get_glyphs_in_class", hb_ot_layout_get_glyphs_in_class}, // HarfBuzzSharp
    {"hb_ot_layout_get_ligature_carets", hb_ot_layout_get_ligature_carets}, // HarfBuzzSharp
    {"hb_ot_layout_get_size_params", hb_ot_layout_get_size_params}, // HarfBuzzSharp
    {"hb_ot_layout_has_glyph_classes", hb_ot_layout_has_glyph_classes}, // HarfBuzzSharp
    {"hb_ot_layout_has_positioning", hb_ot_layout_has_positioning}, // HarfBuzzSharp
    {"hb_ot_layout_has_substitution", hb_ot_layout_has_substitution}, // HarfBuzzSharp
    {"hb_ot_layout_language_find_feature", hb_ot_layout_language_find_feature}, // HarfBuzzSharp
    {"hb_ot_layout_language_get_feature_indexes", hb_ot_layout_language_get_feature_indexes}, // HarfBuzzSharp
    {"hb_ot_layout_language_get_feature_tags", hb_ot_layout_language_get_feature_tags}, // HarfBuzzSharp
    {"hb_ot_layout_language_get_required_feature", hb_ot_layout_language_get_required_feature}, // HarfBuzzSharp
    {"hb_ot_layout_language_get_required_feature_index", hb_ot_layout_language_get_required_feature_index}, // HarfBuzzSharp
    {"hb_ot_layout_lookup_collect_glyphs", hb_ot_layout_lookup_collect_glyphs}, // HarfBuzzSharp
    {"hb_ot_layout_lookup_get_glyph_alternates", hb_ot_layout_lookup_get_glyph_alternates}, // HarfBuzzSharp
    {"hb_ot_layout_lookup_substitute_closure", hb_ot_layout_lookup_substitute_closure}, // HarfBuzzSharp
    {"hb_ot_layout_lookup_would_substitute", hb_ot_layout_lookup_would_substitute}, // HarfBuzzSharp
    {"hb_ot_layout_lookups_substitute_closure", hb_ot_layout_lookups_substitute_closure}, // HarfBuzzSharp
    {"hb_ot_layout_script_get_language_tags", hb_ot_layout_script_get_language_tags}, // HarfBuzzSharp
    {"hb_ot_layout_script_select_language", hb_ot_layout_script_select_language}, // HarfBuzzSharp
    {"hb_ot_layout_table_find_feature_variations", hb_ot_layout_table_find_feature_variations}, // HarfBuzzSharp
    {"hb_ot_layout_table_find_script", hb_ot_layout_table_find_script}, // HarfBuzzSharp
    {"hb_ot_layout_table_get_feature_tags", hb_ot_layout_table_get_feature_tags}, // HarfBuzzSharp
    {"hb_ot_layout_table_get_lookup_count", hb_ot_layout_table_get_lookup_count}, // HarfBuzzSharp
    {"hb_ot_layout_table_get_script_tags", hb_ot_layout_table_get_script_tags}, // HarfBuzzSharp
    {"hb_ot_layout_table_select_script", hb_ot_layout_table_select_script}, // HarfBuzzSharp
    {"hb_ot_math_get_constant", hb_ot_math_get_constant}, // HarfBuzzSharp
    {"hb_ot_math_get_glyph_assembly", hb_ot_math_get_glyph_assembly}, // HarfBuzzSharp
    {"hb_ot_math_get_glyph_italics_correction", hb_ot_math_get_glyph_italics_correction}, // HarfBuzzSharp
    {"hb_ot_math_get_glyph_kerning", hb_ot_math_get_glyph_kerning}, // HarfBuzzSharp
    {"hb_ot_math_get_glyph_top_accent_attachment", hb_ot_math_get_glyph_top_accent_attachment}, // HarfBuzzSharp
    {"hb_ot_math_get_glyph_variants", hb_ot_math_get_glyph_variants}, // HarfBuzzSharp
    {"hb_ot_math_get_min_connector_overlap", hb_ot_math_get_min_connector_overlap}, // HarfBuzzSharp
    {"hb_ot_math_has_data", hb_ot_math_has_data}, // HarfBuzzSharp
    {"hb_ot_math_is_glyph_extended_shape", hb_ot_math_is_glyph_extended_shape}, // HarfBuzzSharp
    {"hb_ot_meta_get_entry_tags", hb_ot_meta_get_entry_tags}, // HarfBuzzSharp
    {"hb_ot_meta_reference_entry", hb_ot_meta_reference_entry}, // HarfBuzzSharp
    {"hb_ot_metrics_get_position", hb_ot_metrics_get_position}, // HarfBuzzSharp
    {"hb_ot_metrics_get_variation", hb_ot_metrics_get_variation}, // HarfBuzzSharp
    {"hb_ot_metrics_get_x_variation", hb_ot_metrics_get_x_variation}, // HarfBuzzSharp
    {"hb_ot_metrics_get_y_variation", hb_ot_metrics_get_y_variation}, // HarfBuzzSharp
    {"hb_ot_name_get_utf16", hb_ot_name_get_utf16}, // HarfBuzzSharp
    {"hb_ot_name_get_utf32", hb_ot_name_get_utf32}, // HarfBuzzSharp
    {"hb_ot_name_get_utf8", hb_ot_name_get_utf8}, // HarfBuzzSharp
    {"hb_ot_name_list_names", hb_ot_name_list_names}, // HarfBuzzSharp
    {"hb_ot_shape_glyphs_closure", hb_ot_shape_glyphs_closure}, // HarfBuzzSharp
    {"hb_ot_shape_plan_collect_lookups", hb_ot_shape_plan_collect_lookups}, // HarfBuzzSharp
    {"hb_ot_tag_to_language", hb_ot_tag_to_language}, // HarfBuzzSharp
    {"hb_ot_tag_to_script", hb_ot_tag_to_script}, // HarfBuzzSharp
    {"hb_ot_tags_from_script_and_language", hb_ot_tags_from_script_and_language}, // HarfBuzzSharp
    {"hb_ot_tags_to_script_and_language", hb_ot_tags_to_script_and_language}, // HarfBuzzSharp
    {"hb_script_from_iso15924_tag", hb_script_from_iso15924_tag}, // HarfBuzzSharp
    {"hb_script_from_string", hb_script_from_string}, // HarfBuzzSharp
    {"hb_script_get_horizontal_direction", hb_script_get_horizontal_direction}, // HarfBuzzSharp
    {"hb_script_to_iso15924_tag", hb_script_to_iso15924_tag}, // HarfBuzzSharp
    {"hb_set_add", hb_set_add}, // HarfBuzzSharp
    {"hb_set_add_range", hb_set_add_range}, // HarfBuzzSharp
    {"hb_set_allocation_successful", hb_set_allocation_successful}, // HarfBuzzSharp
    {"hb_set_clear", hb_set_clear}, // HarfBuzzSharp
    {"hb_set_copy", hb_set_copy}, // HarfBuzzSharp
    {"hb_set_create", hb_set_create}, // HarfBuzzSharp
    {"hb_set_del", hb_set_del}, // HarfBuzzSharp
    {"hb_set_del_range", hb_set_del_range}, // HarfBuzzSharp
    {"hb_set_destroy", hb_set_destroy}, // HarfBuzzSharp
    {"hb_set_get_empty", hb_set_get_empty}, // HarfBuzzSharp
    {"hb_set_get_max", hb_set_get_max}, // HarfBuzzSharp
    {"hb_set_get_min", hb_set_get_min}, // HarfBuzzSharp
    {"hb_set_get_population", hb_set_get_population}, // HarfBuzzSharp
    {"hb_set_has", hb_set_has}, // HarfBuzzSharp
    {"hb_set_intersect", hb_set_intersect}, // HarfBuzzSharp
    {"hb_set_is_empty", hb_set_is_empty}, // HarfBuzzSharp
    {"hb_set_is_equal", hb_set_is_equal}, // HarfBuzzSharp
    {"hb_set_is_subset", hb_set_is_subset}, // HarfBuzzSharp
    {"hb_set_next", hb_set_next}, // HarfBuzzSharp
    {"hb_set_next_range", hb_set_next_range}, // HarfBuzzSharp
    {"hb_set_previous", hb_set_previous}, // HarfBuzzSharp
    {"hb_set_previous_range", hb_set_previous_range}, // HarfBuzzSharp
    {"hb_set_reference", hb_set_reference}, // HarfBuzzSharp
    {"hb_set_set", hb_set_set}, // HarfBuzzSharp
    {"hb_set_subtract", hb_set_subtract}, // HarfBuzzSharp
    {"hb_set_symmetric_difference", hb_set_symmetric_difference}, // HarfBuzzSharp
    {"hb_set_union", hb_set_union}, // HarfBuzzSharp
    {"hb_shape", hb_shape}, // HarfBuzzSharp
    {"hb_shape_full", hb_shape_full}, // HarfBuzzSharp
    {"hb_shape_list_shapers", hb_shape_list_shapers}, // HarfBuzzSharp
    {"hb_tag_from_string", hb_tag_from_string}, // HarfBuzzSharp
    {"hb_tag_to_string", hb_tag_to_string}, // HarfBuzzSharp
    {"hb_unicode_combining_class", hb_unicode_combining_class}, // HarfBuzzSharp
    {"hb_unicode_compose", hb_unicode_compose}, // HarfBuzzSharp
    {"hb_unicode_decompose", hb_unicode_decompose}, // HarfBuzzSharp
    {"hb_unicode_funcs_create", hb_unicode_funcs_create}, // HarfBuzzSharp
    {"hb_unicode_funcs_destroy", hb_unicode_funcs_destroy}, // HarfBuzzSharp
    {"hb_unicode_funcs_get_default", hb_unicode_funcs_get_default}, // HarfBuzzSharp
    {"hb_unicode_funcs_get_empty", hb_unicode_funcs_get_empty}, // HarfBuzzSharp
    {"hb_unicode_funcs_get_parent", hb_unicode_funcs_get_parent}, // HarfBuzzSharp
    {"hb_unicode_funcs_is_immutable", hb_unicode_funcs_is_immutable}, // HarfBuzzSharp
    {"hb_unicode_funcs_make_immutable", hb_unicode_funcs_make_immutable}, // HarfBuzzSharp
    {"hb_unicode_funcs_reference", hb_unicode_funcs_reference}, // HarfBuzzSharp
    {"hb_unicode_funcs_set_combining_class_func", hb_unicode_funcs_set_combining_class_func}, // HarfBuzzSharp
    {"hb_unicode_funcs_set_compose_func", hb_unicode_funcs_set_compose_func}, // HarfBuzzSharp
    {"hb_unicode_funcs_set_decompose_func", hb_unicode_funcs_set_decompose_func}, // HarfBuzzSharp
    {"hb_unicode_funcs_set_general_category_func", hb_unicode_funcs_set_general_category_func}, // HarfBuzzSharp
    {"hb_unicode_funcs_set_mirroring_func", hb_unicode_funcs_set_mirroring_func}, // HarfBuzzSharp
    {"hb_unicode_funcs_set_script_func", hb_unicode_funcs_set_script_func}, // HarfBuzzSharp
    {"hb_unicode_general_category", hb_unicode_general_category}, // HarfBuzzSharp
    {"hb_unicode_mirroring", hb_unicode_mirroring}, // HarfBuzzSharp
    {"hb_unicode_script", hb_unicode_script}, // HarfBuzzSharp
    {"hb_variation_from_string", hb_variation_from_string}, // HarfBuzzSharp
    {"hb_variation_to_string", hb_variation_to_string}, // HarfBuzzSharp
    {"hb_version", hb_version}, // HarfBuzzSharp
    {"hb_version_atleast", hb_version_atleast}, // HarfBuzzSharp
    {"hb_version_string", hb_version_string}, // HarfBuzzSharp
    {NULL, NULL}
};
static PinvokeImport libSystem_Native_imports [] = {
    {"SystemNative_Access", SystemNative_Access}, // System.Private.CoreLib
    {"SystemNative_AlignedAlloc", SystemNative_AlignedAlloc}, // System.Private.CoreLib
    {"SystemNative_AlignedFree", SystemNative_AlignedFree}, // System.Private.CoreLib
    {"SystemNative_AlignedRealloc", SystemNative_AlignedRealloc}, // System.Private.CoreLib
    {"SystemNative_Calloc", SystemNative_Calloc}, // System.Private.CoreLib
    {"SystemNative_CanGetHiddenFlag", SystemNative_CanGetHiddenFlag}, // System.Private.CoreLib
    {"SystemNative_ChDir", SystemNative_ChDir}, // System.Private.CoreLib
    {"SystemNative_ChMod", SystemNative_ChMod}, // System.Private.CoreLib
    {"SystemNative_Close", SystemNative_Close}, // System.Private.CoreLib
    {"SystemNative_CloseDir", SystemNative_CloseDir}, // System.Private.CoreLib
    {"SystemNative_ConvertErrorPalToPlatform", SystemNative_ConvertErrorPalToPlatform}, // System.Console, System.IO.Compression.ZipFile, System.IO.MemoryMappedFiles, System.Net.Primitives, System.Private.CoreLib
    {"SystemNative_ConvertErrorPlatformToPal", SystemNative_ConvertErrorPlatformToPal}, // System.Console, System.IO.Compression.ZipFile, System.IO.MemoryMappedFiles, System.Net.Primitives, System.Private.CoreLib
    {"SystemNative_CopyFile", SystemNative_CopyFile}, // System.Private.CoreLib
    {"SystemNative_Dup", SystemNative_Dup}, // System.Console
    {"SystemNative_FAllocate", SystemNative_FAllocate}, // System.Private.CoreLib
    {"SystemNative_FChflags", SystemNative_FChflags}, // System.Private.CoreLib
    {"SystemNative_FChMod", SystemNative_FChMod}, // System.Private.CoreLib
    {"SystemNative_FcntlSetFD", SystemNative_FcntlSetFD}, // System.IO.MemoryMappedFiles
    {"SystemNative_FLock", SystemNative_FLock}, // System.Private.CoreLib
    {"SystemNative_Free", SystemNative_Free}, // System.Private.CoreLib
    {"SystemNative_FreeEnviron", SystemNative_FreeEnviron}, // System.Private.CoreLib
    {"SystemNative_FStat", SystemNative_FStat}, // System.IO.Compression.ZipFile, System.IO.MemoryMappedFiles, System.Private.CoreLib
    {"SystemNative_FSync", SystemNative_FSync}, // System.Private.CoreLib
    {"SystemNative_FTruncate", SystemNative_FTruncate}, // System.IO.MemoryMappedFiles, System.Private.CoreLib
    {"SystemNative_FUTimens", SystemNative_FUTimens}, // System.Private.CoreLib
    {"SystemNative_GetAddressFamily", SystemNative_GetAddressFamily}, // System.Net.Primitives
    {"SystemNative_GetCpuUtilization", SystemNative_GetCpuUtilization}, // System.Private.CoreLib
    {"SystemNative_GetCryptographicallySecureRandomBytes", SystemNative_GetCryptographicallySecureRandomBytes}, // System.Private.CoreLib, System.Security.Cryptography
    {"SystemNative_GetCwd", SystemNative_GetCwd}, // System.Private.CoreLib
    {"SystemNative_GetDefaultSearchOrderPseudoHandle", SystemNative_GetDefaultSearchOrderPseudoHandle}, // System.Private.CoreLib
    {"SystemNative_GetEnv", SystemNative_GetEnv}, // System.Private.CoreLib
    {"SystemNative_GetEnviron", SystemNative_GetEnviron}, // System.Private.CoreLib
    {"SystemNative_GetErrNo", SystemNative_GetErrNo}, // System.Private.CoreLib
    {"SystemNative_GetFileSystemType", SystemNative_GetFileSystemType}, // System.Private.CoreLib
    {"SystemNative_GetIPv4Address", SystemNative_GetIPv4Address}, // System.Net.Primitives
    {"SystemNative_GetIPv6Address", SystemNative_GetIPv6Address}, // System.Net.Primitives
    {"SystemNative_GetNonCryptographicallySecureRandomBytes", SystemNative_GetNonCryptographicallySecureRandomBytes}, // System.Private.CoreLib
    {"SystemNative_GetPort", SystemNative_GetPort}, // System.Net.Primitives
    {"SystemNative_GetReadDirRBufferSize", SystemNative_GetReadDirRBufferSize}, // System.Private.CoreLib
    {"SystemNative_GetSocketAddressSizes", SystemNative_GetSocketAddressSizes}, // System.Net.Primitives
    {"SystemNative_GetSystemTimeAsTicks", SystemNative_GetSystemTimeAsTicks}, // System.Private.CoreLib
    {"SystemNative_GetTimestamp", SystemNative_GetTimestamp}, // System.Private.CoreLib
    {"SystemNative_GetTimeZoneData", SystemNative_GetTimeZoneData}, // System.Private.CoreLib
    {"SystemNative_LChflags", SystemNative_LChflags}, // System.Private.CoreLib
    {"SystemNative_LChflagsCanSetHiddenFlag", SystemNative_LChflagsCanSetHiddenFlag}, // System.Private.CoreLib
    {"SystemNative_Link", SystemNative_Link}, // System.Private.CoreLib
    {"SystemNative_LockFileRegion", SystemNative_LockFileRegion}, // System.Private.CoreLib
    {"SystemNative_Log", SystemNative_Log}, // System.Private.CoreLib
    {"SystemNative_LogError", SystemNative_LogError}, // System.Private.CoreLib
    {"SystemNative_LowLevelMonitor_Acquire", SystemNative_LowLevelMonitor_Acquire}, // System.Private.CoreLib
    {"SystemNative_LowLevelMonitor_Create", SystemNative_LowLevelMonitor_Create}, // System.Private.CoreLib
    {"SystemNative_LowLevelMonitor_Destroy", SystemNative_LowLevelMonitor_Destroy}, // System.Private.CoreLib
    {"SystemNative_LowLevelMonitor_Release", SystemNative_LowLevelMonitor_Release}, // System.Private.CoreLib
    {"SystemNative_LowLevelMonitor_Signal_Release", SystemNative_LowLevelMonitor_Signal_Release}, // System.Private.CoreLib
    {"SystemNative_LowLevelMonitor_TimedWait", SystemNative_LowLevelMonitor_TimedWait}, // System.Private.CoreLib
    {"SystemNative_LowLevelMonitor_Wait", SystemNative_LowLevelMonitor_Wait}, // System.Private.CoreLib
    {"SystemNative_LSeek", SystemNative_LSeek}, // System.Private.CoreLib
    {"SystemNative_LStat", SystemNative_LStat}, // System.Private.CoreLib
    {"SystemNative_MAdvise", SystemNative_MAdvise}, // System.IO.MemoryMappedFiles
    {"SystemNative_Malloc", SystemNative_Malloc}, // System.Private.CoreLib
    {"SystemNative_MkDir", SystemNative_MkDir}, // System.Private.CoreLib
    {"SystemNative_MkdTemp", SystemNative_MkdTemp}, // System.Private.CoreLib
    {"SystemNative_MksTemps", SystemNative_MksTemps}, // System.Private.CoreLib
    {"SystemNative_MMap", SystemNative_MMap}, // System.IO.MemoryMappedFiles
    {"SystemNative_MSync", SystemNative_MSync}, // System.IO.MemoryMappedFiles
    {"SystemNative_MUnmap", SystemNative_MUnmap}, // System.IO.MemoryMappedFiles
    {"SystemNative_Open", SystemNative_Open}, // System.Private.CoreLib
    {"SystemNative_OpenDir", SystemNative_OpenDir}, // System.Private.CoreLib
    {"SystemNative_PosixFAdvise", SystemNative_PosixFAdvise}, // System.Private.CoreLib
    {"SystemNative_PRead", SystemNative_PRead}, // System.Private.CoreLib
    {"SystemNative_PReadV", SystemNative_PReadV}, // System.Private.CoreLib
    {"SystemNative_PWrite", SystemNative_PWrite}, // System.Private.CoreLib
    {"SystemNative_PWriteV", SystemNative_PWriteV}, // System.Private.CoreLib
    {"SystemNative_Read", SystemNative_Read}, // System.Private.CoreLib
    {"SystemNative_ReadDirR", SystemNative_ReadDirR}, // System.Private.CoreLib
    {"SystemNative_ReadLink", SystemNative_ReadLink}, // System.Private.CoreLib
    {"SystemNative_Realloc", SystemNative_Realloc}, // System.Private.CoreLib
    {"SystemNative_Rename", SystemNative_Rename}, // System.Private.CoreLib
    {"SystemNative_RmDir", SystemNative_RmDir}, // System.Private.CoreLib
    {"SystemNative_SchedGetCpu", SystemNative_SchedGetCpu}, // System.Private.CoreLib
    {"SystemNative_SetAddressFamily", SystemNative_SetAddressFamily}, // System.Net.Primitives
    {"SystemNative_SetErrNo", SystemNative_SetErrNo}, // System.Private.CoreLib
    {"SystemNative_SetIPv4Address", SystemNative_SetIPv4Address}, // System.Net.Primitives
    {"SystemNative_SetIPv6Address", SystemNative_SetIPv6Address}, // System.Net.Primitives
    {"SystemNative_SetPort", SystemNative_SetPort}, // System.Net.Primitives
    {"SystemNative_ShmOpen", SystemNative_ShmOpen}, // System.IO.MemoryMappedFiles
    {"SystemNative_ShmUnlink", SystemNative_ShmUnlink}, // System.IO.MemoryMappedFiles
    {"SystemNative_Stat", SystemNative_Stat}, // System.IO.Compression.ZipFile, System.Private.CoreLib
    {"SystemNative_StrErrorR", SystemNative_StrErrorR}, // System.Console, System.IO.Compression.ZipFile, System.IO.MemoryMappedFiles, System.Net.Primitives, System.Private.CoreLib
    {"SystemNative_SymLink", SystemNative_SymLink}, // System.Private.CoreLib
    {"SystemNative_SysConf", SystemNative_SysConf}, // System.IO.MemoryMappedFiles, System.Private.CoreLib
    {"SystemNative_SysLog", SystemNative_SysLog}, // System.Private.CoreLib
    {"SystemNative_TryGetUInt32OSThreadId", SystemNative_TryGetUInt32OSThreadId}, // System.Private.CoreLib
    {"SystemNative_Unlink", SystemNative_Unlink}, // System.IO.MemoryMappedFiles, System.Private.CoreLib
    {"SystemNative_UTimensat", SystemNative_UTimensat}, // System.Private.CoreLib
    {"SystemNative_Write", SystemNative_Write}, // System.Console, System.Private.CoreLib
    {NULL, NULL}
};
static PinvokeImport libSystem_IO_Compression_Native_imports [] = {
    {"CompressionNative_Crc32", CompressionNative_Crc32}, // System.IO.Compression
    {"CompressionNative_Deflate", CompressionNative_Deflate}, // System.IO.Compression, System.Net.WebSockets
    {"CompressionNative_DeflateEnd", CompressionNative_DeflateEnd}, // System.IO.Compression, System.Net.WebSockets
    {"CompressionNative_DeflateInit2_", CompressionNative_DeflateInit2_}, // System.IO.Compression, System.Net.WebSockets
    {"CompressionNative_Inflate", CompressionNative_Inflate}, // System.IO.Compression, System.Net.WebSockets
    {"CompressionNative_InflateEnd", CompressionNative_InflateEnd}, // System.IO.Compression, System.Net.WebSockets
    {"CompressionNative_InflateInit2_", CompressionNative_InflateInit2_}, // System.IO.Compression, System.Net.WebSockets
    {NULL, NULL}
};
static PinvokeImport libSystem_Globalization_Native_imports [] = {
    {"GlobalizationNative_ChangeCase", GlobalizationNative_ChangeCase}, // System.Private.CoreLib
    {"GlobalizationNative_ChangeCaseInvariant", GlobalizationNative_ChangeCaseInvariant}, // System.Private.CoreLib
    {"GlobalizationNative_ChangeCaseTurkish", GlobalizationNative_ChangeCaseTurkish}, // System.Private.CoreLib
    {"GlobalizationNative_CloseSortHandle", GlobalizationNative_CloseSortHandle}, // System.Private.CoreLib
    {"GlobalizationNative_CompareString", GlobalizationNative_CompareString}, // System.Private.CoreLib
    {"GlobalizationNative_EndsWith", GlobalizationNative_EndsWith}, // System.Private.CoreLib
    {"GlobalizationNative_EnumCalendarInfo", GlobalizationNative_EnumCalendarInfo}, // System.Private.CoreLib
    {"GlobalizationNative_GetCalendarInfo", GlobalizationNative_GetCalendarInfo}, // System.Private.CoreLib
    {"GlobalizationNative_GetCalendars", GlobalizationNative_GetCalendars}, // System.Private.CoreLib
    {"GlobalizationNative_GetDefaultLocaleName", GlobalizationNative_GetDefaultLocaleName}, // System.Private.CoreLib
    {"GlobalizationNative_GetICUVersion", GlobalizationNative_GetICUVersion}, // System.Private.CoreLib
    {"GlobalizationNative_GetJapaneseEraStartDate", GlobalizationNative_GetJapaneseEraStartDate}, // System.Private.CoreLib
    {"GlobalizationNative_GetLatestJapaneseEra", GlobalizationNative_GetLatestJapaneseEra}, // System.Private.CoreLib
    {"GlobalizationNative_GetLocaleInfoGroupingSizes", GlobalizationNative_GetLocaleInfoGroupingSizes}, // System.Private.CoreLib
    {"GlobalizationNative_GetLocaleInfoInt", GlobalizationNative_GetLocaleInfoInt}, // System.Private.CoreLib
    {"GlobalizationNative_GetLocaleInfoString", GlobalizationNative_GetLocaleInfoString}, // System.Private.CoreLib
    {"GlobalizationNative_GetLocaleName", GlobalizationNative_GetLocaleName}, // System.Private.CoreLib
    {"GlobalizationNative_GetLocales", GlobalizationNative_GetLocales}, // System.Private.CoreLib
    {"GlobalizationNative_GetLocaleTimeFormat", GlobalizationNative_GetLocaleTimeFormat}, // System.Private.CoreLib
    {"GlobalizationNative_GetSortHandle", GlobalizationNative_GetSortHandle}, // System.Private.CoreLib
    {"GlobalizationNative_GetSortKey", GlobalizationNative_GetSortKey}, // System.Private.CoreLib
    {"GlobalizationNative_GetSortVersion", GlobalizationNative_GetSortVersion}, // System.Private.CoreLib
    {"GlobalizationNative_IndexOf", GlobalizationNative_IndexOf}, // System.Private.CoreLib
    {"GlobalizationNative_InitICUFunctions", GlobalizationNative_InitICUFunctions}, // System.Private.CoreLib
    {"GlobalizationNative_InitOrdinalCasingPage", GlobalizationNative_InitOrdinalCasingPage}, // System.Private.CoreLib
    {"GlobalizationNative_IsNormalized", GlobalizationNative_IsNormalized}, // System.Private.CoreLib
    {"GlobalizationNative_IsPredefinedLocale", GlobalizationNative_IsPredefinedLocale}, // System.Private.CoreLib
    {"GlobalizationNative_LastIndexOf", GlobalizationNative_LastIndexOf}, // System.Private.CoreLib
    {"GlobalizationNative_LoadICU", GlobalizationNative_LoadICU}, // System.Private.CoreLib
    {"GlobalizationNative_NormalizeString", GlobalizationNative_NormalizeString}, // System.Private.CoreLib
    {"GlobalizationNative_StartsWith", GlobalizationNative_StartsWith}, // System.Private.CoreLib
    {"GlobalizationNative_ToAscii", GlobalizationNative_ToAscii}, // System.Private.CoreLib
    {"GlobalizationNative_ToUnicode", GlobalizationNative_ToUnicode}, // System.Private.CoreLib
    {NULL, NULL}
};
static PinvokeImport _2A__imports [] = {
    {"pthread_self", pthread_self}, // Avalonia.Browser
    {NULL, NULL}
};

static void *pinvoke_tables[] = {
    (void*)libSkiaSharp_imports, (void*)libHarfBuzzSharp_imports, (void*)libSystem_Native_imports, (void*)libSystem_IO_Compression_Native_imports, (void*)libSystem_Globalization_Native_imports, (void*)_2A__imports
};

static char *pinvoke_names[] =  {
    "libSkiaSharp", "libHarfBuzzSharp", "libSystem.Native", "libSystem.IO.Compression.Native", "libSystem.Globalization.Native", "*"
};
#include <mono/utils/details/mono-error-types.h>
                #include <mono/metadata/assembly.h>
                #include <mono/utils/mono-error.h>
                #include <mono/metadata/object.h>
                #include <mono/utils/details/mono-logger-types.h>
                #include "runtime.h"
                InterpFtnDesc wasm_native_to_interp_ftndescs[63] = {};
typedef void (*WasmInterpEntrySig_0) (int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_LoadAssemblyAndGetFunctionPointer (void * arg0, void * arg1, void * arg2, void * arg3, void * arg4, void * arg5) { 
  int32_t res;
  if (!(WasmInterpEntrySig_0)wasm_native_to_interp_ftndescs [0].func) {
   mono_wasm_marshal_get_managed_wrapper ("System.Private.CoreLib","Internal.Runtime.InteropServices", "ComponentActivator", "LoadAssemblyAndGetFunctionPointer", 6);
  }
  ((WasmInterpEntrySig_0)wasm_native_to_interp_ftndescs [0].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [0].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_1) (int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_LoadAssembly (void * arg0, void * arg1, void * arg2) { 
  int32_t res;
  if (!(WasmInterpEntrySig_1)wasm_native_to_interp_ftndescs [1].func) {
   mono_wasm_marshal_get_managed_wrapper ("System.Private.CoreLib","Internal.Runtime.InteropServices", "ComponentActivator", "LoadAssembly", 3);
  }
  ((WasmInterpEntrySig_1)wasm_native_to_interp_ftndescs [1].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [1].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_2) (int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_LoadAssemblyBytes (void * arg0, void * arg1, void * arg2, void * arg3, void * arg4, void * arg5) { 
  int32_t res;
  if (!(WasmInterpEntrySig_2)wasm_native_to_interp_ftndescs [2].func) {
   mono_wasm_marshal_get_managed_wrapper ("System.Private.CoreLib","Internal.Runtime.InteropServices", "ComponentActivator", "LoadAssemblyBytes", 6);
  }
  ((WasmInterpEntrySig_2)wasm_native_to_interp_ftndescs [2].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [2].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_3) (int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_GetFunctionPointer (void * arg0, void * arg1, void * arg2, void * arg3, void * arg4, void * arg5) { 
  int32_t res;
  if (!(WasmInterpEntrySig_3)wasm_native_to_interp_ftndescs [3].func) {
   mono_wasm_marshal_get_managed_wrapper ("System.Private.CoreLib","Internal.Runtime.InteropServices", "ComponentActivator", "GetFunctionPointer", 6);
  }
  ((WasmInterpEntrySig_3)wasm_native_to_interp_ftndescs [3].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [3].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_4) (int*, int*, int*);
void wasm_native_to_interp_System_Globalization_System_Private_CoreLib_CalendarData_EnumCalendarInfoCallback (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_4)wasm_native_to_interp_ftndescs [4].func) {
   mono_wasm_marshal_get_managed_wrapper ("System.Private.CoreLib","System.Globalization", "CalendarData", "EnumCalendarInfoCallback", 2);
  }
  ((WasmInterpEntrySig_4)wasm_native_to_interp_ftndescs [4].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [4].arg);
}

typedef void (*WasmInterpEntrySig_5) (int*);
void wasm_native_to_interp_System_Threading_System_Private_CoreLib_ThreadPool_BackgroundJobHandler () { 
  if (!(WasmInterpEntrySig_5)wasm_native_to_interp_ftndescs [5].func) {
   mono_wasm_marshal_get_managed_wrapper ("System.Private.CoreLib","System.Threading", "ThreadPool", "BackgroundJobHandler", 0);
  }
  ((WasmInterpEntrySig_5)wasm_native_to_interp_ftndescs [5].func) (wasm_native_to_interp_ftndescs [5].arg);
}

typedef void (*WasmInterpEntrySig_6) (int*);
void wasm_native_to_interp_System_Threading_System_Private_CoreLib_TimerQueue_TimerHandler () { 
  if (!(WasmInterpEntrySig_6)wasm_native_to_interp_ftndescs [6].func) {
   mono_wasm_marshal_get_managed_wrapper ("System.Private.CoreLib","System.Threading", "TimerQueue", "TimerHandler", 0);
  }
  ((WasmInterpEntrySig_6)wasm_native_to_interp_ftndescs [6].func) (wasm_native_to_interp_ftndescs [6].arg);
}

typedef void (*WasmInterpEntrySig_7) (int*, int*);
void wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ReleaseDelegateProxyImplementation (void * arg0) { 
  if (!(WasmInterpEntrySig_7)wasm_native_to_interp_ftndescs [7].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "ReleaseDelegateProxyImplementation", 1);
  }
  ((WasmInterpEntrySig_7)wasm_native_to_interp_ftndescs [7].func) ((int*)&arg0, wasm_native_to_interp_ftndescs [7].arg);
}

typedef void (*WasmInterpEntrySig_8) (int*, int*, int*, int*, int*);
void * wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GetTableDelegateProxyImplementation (void * arg0, uint32_t arg1, void * arg2) { 
  void * res;
  if (!(WasmInterpEntrySig_8)wasm_native_to_interp_ftndescs [8].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GetTableDelegateProxyImplementation", 3);
  }
  ((WasmInterpEntrySig_8)wasm_native_to_interp_ftndescs [8].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [8].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_9) (int*, int*);
void wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ReleaseDelegateProxyImplementationForMulti (void * arg0) { 
  if (!(WasmInterpEntrySig_9)wasm_native_to_interp_ftndescs [9].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "ReleaseDelegateProxyImplementationForMulti", 1);
  }
  ((WasmInterpEntrySig_9)wasm_native_to_interp_ftndescs [9].func) ((int*)&arg0, wasm_native_to_interp_ftndescs [9].arg);
}

typedef void (*WasmInterpEntrySig_10) (int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_FontExtentsProxyImplementation (void * arg0, void * arg1, void * arg2, void * arg3) { 
  int32_t res;
  if (!(WasmInterpEntrySig_10)wasm_native_to_interp_ftndescs [10].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "FontExtentsProxyImplementation", 4);
  }
  ((WasmInterpEntrySig_10)wasm_native_to_interp_ftndescs [10].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, wasm_native_to_interp_ftndescs [10].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_11) (int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_NominalGlyphProxyImplementation (void * arg0, void * arg1, uint32_t arg2, void * arg3, void * arg4) { 
  int32_t res;
  if (!(WasmInterpEntrySig_11)wasm_native_to_interp_ftndescs [11].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "NominalGlyphProxyImplementation", 5);
  }
  ((WasmInterpEntrySig_11)wasm_native_to_interp_ftndescs [11].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, wasm_native_to_interp_ftndescs [11].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_12) (int*, int*, int*, int*, int*, int*, int*, int*, int*, int*);
uint32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_NominalGlyphsProxyImplementation (void * arg0, void * arg1, uint32_t arg2, void * arg3, uint32_t arg4, void * arg5, uint32_t arg6, void * arg7) { 
  uint32_t res;
  if (!(WasmInterpEntrySig_12)wasm_native_to_interp_ftndescs [12].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "NominalGlyphsProxyImplementation", 8);
  }
  ((WasmInterpEntrySig_12)wasm_native_to_interp_ftndescs [12].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, (int*)&arg6, (int*)&arg7, wasm_native_to_interp_ftndescs [12].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_13) (int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_VariationGlyphProxyImplementation (void * arg0, void * arg1, uint32_t arg2, uint32_t arg3, void * arg4, void * arg5) { 
  int32_t res;
  if (!(WasmInterpEntrySig_13)wasm_native_to_interp_ftndescs [13].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "VariationGlyphProxyImplementation", 6);
  }
  ((WasmInterpEntrySig_13)wasm_native_to_interp_ftndescs [13].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [13].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_14) (int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphAdvanceProxyImplementation (void * arg0, void * arg1, uint32_t arg2, void * arg3) { 
  int32_t res;
  if (!(WasmInterpEntrySig_14)wasm_native_to_interp_ftndescs [14].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphAdvanceProxyImplementation", 4);
  }
  ((WasmInterpEntrySig_14)wasm_native_to_interp_ftndescs [14].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, wasm_native_to_interp_ftndescs [14].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_15) (int*, int*, int*, int*, int*, int*, int*, int*, int*);
void wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphAdvancesProxyImplementation (void * arg0, void * arg1, uint32_t arg2, void * arg3, uint32_t arg4, void * arg5, uint32_t arg6, void * arg7) { 
  if (!(WasmInterpEntrySig_15)wasm_native_to_interp_ftndescs [15].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphAdvancesProxyImplementation", 8);
  }
  ((WasmInterpEntrySig_15)wasm_native_to_interp_ftndescs [15].func) ((int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, (int*)&arg6, (int*)&arg7, wasm_native_to_interp_ftndescs [15].arg);
}

typedef void (*WasmInterpEntrySig_16) (int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphOriginProxyImplementation (void * arg0, void * arg1, uint32_t arg2, void * arg3, void * arg4, void * arg5) { 
  int32_t res;
  if (!(WasmInterpEntrySig_16)wasm_native_to_interp_ftndescs [16].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphOriginProxyImplementation", 6);
  }
  ((WasmInterpEntrySig_16)wasm_native_to_interp_ftndescs [16].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [16].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_17) (int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphKerningProxyImplementation (void * arg0, void * arg1, uint32_t arg2, uint32_t arg3, void * arg4) { 
  int32_t res;
  if (!(WasmInterpEntrySig_17)wasm_native_to_interp_ftndescs [17].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphKerningProxyImplementation", 5);
  }
  ((WasmInterpEntrySig_17)wasm_native_to_interp_ftndescs [17].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, wasm_native_to_interp_ftndescs [17].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_18) (int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphExtentsProxyImplementation (void * arg0, void * arg1, uint32_t arg2, void * arg3, void * arg4) { 
  int32_t res;
  if (!(WasmInterpEntrySig_18)wasm_native_to_interp_ftndescs [18].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphExtentsProxyImplementation", 5);
  }
  ((WasmInterpEntrySig_18)wasm_native_to_interp_ftndescs [18].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, wasm_native_to_interp_ftndescs [18].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_19) (int*, int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphContourPointProxyImplementation (void * arg0, void * arg1, uint32_t arg2, uint32_t arg3, void * arg4, void * arg5, void * arg6) { 
  int32_t res;
  if (!(WasmInterpEntrySig_19)wasm_native_to_interp_ftndescs [19].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphContourPointProxyImplementation", 7);
  }
  ((WasmInterpEntrySig_19)wasm_native_to_interp_ftndescs [19].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, (int*)&arg6, wasm_native_to_interp_ftndescs [19].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_20) (int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphNameProxyImplementation (void * arg0, void * arg1, uint32_t arg2, void * arg3, uint32_t arg4, void * arg5) { 
  int32_t res;
  if (!(WasmInterpEntrySig_20)wasm_native_to_interp_ftndescs [20].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphNameProxyImplementation", 6);
  }
  ((WasmInterpEntrySig_20)wasm_native_to_interp_ftndescs [20].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [20].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_21) (int*, int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphFromNameProxyImplementation (void * arg0, void * arg1, void * arg2, int32_t arg3, void * arg4, void * arg5) { 
  int32_t res;
  if (!(WasmInterpEntrySig_21)wasm_native_to_interp_ftndescs [21].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GlyphFromNameProxyImplementation", 6);
  }
  ((WasmInterpEntrySig_21)wasm_native_to_interp_ftndescs [21].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [21].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_22) (int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_CombiningClassProxyImplementation (void * arg0, uint32_t arg1, void * arg2) { 
  int32_t res;
  if (!(WasmInterpEntrySig_22)wasm_native_to_interp_ftndescs [22].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "CombiningClassProxyImplementation", 3);
  }
  ((WasmInterpEntrySig_22)wasm_native_to_interp_ftndescs [22].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [22].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_23) (int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GeneralCategoryProxyImplementation (void * arg0, uint32_t arg1, void * arg2) { 
  int32_t res;
  if (!(WasmInterpEntrySig_23)wasm_native_to_interp_ftndescs [23].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "GeneralCategoryProxyImplementation", 3);
  }
  ((WasmInterpEntrySig_23)wasm_native_to_interp_ftndescs [23].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [23].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_24) (int*, int*, int*, int*, int*);
uint32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_MirroringProxyImplementation (void * arg0, uint32_t arg1, void * arg2) { 
  uint32_t res;
  if (!(WasmInterpEntrySig_24)wasm_native_to_interp_ftndescs [24].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "MirroringProxyImplementation", 3);
  }
  ((WasmInterpEntrySig_24)wasm_native_to_interp_ftndescs [24].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [24].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_25) (int*, int*, int*, int*, int*);
uint32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ScriptProxyImplementation (void * arg0, uint32_t arg1, void * arg2) { 
  uint32_t res;
  if (!(WasmInterpEntrySig_25)wasm_native_to_interp_ftndescs [25].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "ScriptProxyImplementation", 3);
  }
  ((WasmInterpEntrySig_25)wasm_native_to_interp_ftndescs [25].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [25].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_26) (int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ComposeProxyImplementation (void * arg0, uint32_t arg1, uint32_t arg2, void * arg3, void * arg4) { 
  int32_t res;
  if (!(WasmInterpEntrySig_26)wasm_native_to_interp_ftndescs [26].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "ComposeProxyImplementation", 5);
  }
  ((WasmInterpEntrySig_26)wasm_native_to_interp_ftndescs [26].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, wasm_native_to_interp_ftndescs [26].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_27) (int*, int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_DecomposeProxyImplementation (void * arg0, uint32_t arg1, void * arg2, void * arg3, void * arg4) { 
  int32_t res;
  if (!(WasmInterpEntrySig_27)wasm_native_to_interp_ftndescs [27].func) {
   mono_wasm_marshal_get_managed_wrapper ("HarfBuzzSharp","HarfBuzzSharp", "DelegateProxies", "DecomposeProxyImplementation", 5);
  }
  ((WasmInterpEntrySig_27)wasm_native_to_interp_ftndescs [27].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, wasm_native_to_interp_ftndescs [27].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_28) (int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_MicroCom_Runtime_MicroCom_Runtime_MicroComVtblBase_QueryInterface (void * arg0, void * arg1, void * arg2) { 
  int32_t res;
  if (!(WasmInterpEntrySig_28)wasm_native_to_interp_ftndescs [28].func) {
   mono_wasm_marshal_get_managed_wrapper ("MicroCom.Runtime","MicroCom.Runtime", "MicroComVtblBase", "QueryInterface", 3);
  }
  ((WasmInterpEntrySig_28)wasm_native_to_interp_ftndescs [28].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [28].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_29) (int*, int*, int*);
int32_t wasm_native_to_interp_MicroCom_Runtime_MicroCom_Runtime_MicroComVtblBase_AddRef (void * arg0) { 
  int32_t res;
  if (!(WasmInterpEntrySig_29)wasm_native_to_interp_ftndescs [29].func) {
   mono_wasm_marshal_get_managed_wrapper ("MicroCom.Runtime","MicroCom.Runtime", "MicroComVtblBase", "AddRef", 1);
  }
  ((WasmInterpEntrySig_29)wasm_native_to_interp_ftndescs [29].func) ((int*)&res, (int*)&arg0, wasm_native_to_interp_ftndescs [29].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_30) (int*, int*, int*);
int32_t wasm_native_to_interp_MicroCom_Runtime_MicroCom_Runtime_MicroComVtblBase_Release (void * arg0) { 
  int32_t res;
  if (!(WasmInterpEntrySig_30)wasm_native_to_interp_ftndescs [30].func) {
   mono_wasm_marshal_get_managed_wrapper ("MicroCom.Runtime","MicroCom.Runtime", "MicroComVtblBase", "Release", 1);
  }
  ((WasmInterpEntrySig_30)wasm_native_to_interp_ftndescs [30].func) ((int*)&res, (int*)&arg0, wasm_native_to_interp_ftndescs [30].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_31) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKBitmapReleaseDelegateProxyImplementation (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_31)wasm_native_to_interp_ftndescs [31].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "SKBitmapReleaseDelegateProxyImplementation", 2);
  }
  ((WasmInterpEntrySig_31)wasm_native_to_interp_ftndescs [31].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [31].arg);
}

typedef void (*WasmInterpEntrySig_32) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKDataReleaseDelegateProxyImplementation (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_32)wasm_native_to_interp_ftndescs [32].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "SKDataReleaseDelegateProxyImplementation", 2);
  }
  ((WasmInterpEntrySig_32)wasm_native_to_interp_ftndescs [32].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [32].arg);
}

typedef void (*WasmInterpEntrySig_33) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKImageRasterReleaseDelegateProxyImplementationForCoTaskMem (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_33)wasm_native_to_interp_ftndescs [33].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "SKImageRasterReleaseDelegateProxyImplementationForCoTaskMem", 2);
  }
  ((WasmInterpEntrySig_33)wasm_native_to_interp_ftndescs [33].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [33].arg);
}

typedef void (*WasmInterpEntrySig_34) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKImageRasterReleaseDelegateProxyImplementation (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_34)wasm_native_to_interp_ftndescs [34].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "SKImageRasterReleaseDelegateProxyImplementation", 2);
  }
  ((WasmInterpEntrySig_34)wasm_native_to_interp_ftndescs [34].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [34].arg);
}

typedef void (*WasmInterpEntrySig_35) (int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKImageTextureReleaseDelegateProxyImplementation (void * arg0) { 
  if (!(WasmInterpEntrySig_35)wasm_native_to_interp_ftndescs [35].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "SKImageTextureReleaseDelegateProxyImplementation", 1);
  }
  ((WasmInterpEntrySig_35)wasm_native_to_interp_ftndescs [35].func) ((int*)&arg0, wasm_native_to_interp_ftndescs [35].arg);
}

typedef void (*WasmInterpEntrySig_36) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKSurfaceReleaseDelegateProxyImplementation (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_36)wasm_native_to_interp_ftndescs [36].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "SKSurfaceReleaseDelegateProxyImplementation", 2);
  }
  ((WasmInterpEntrySig_36)wasm_native_to_interp_ftndescs [36].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [36].arg);
}

typedef void (*WasmInterpEntrySig_37) (int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_GRGlGetProcDelegateProxyImplementation (void * arg0, void * arg1) { 
  void * res;
  if (!(WasmInterpEntrySig_37)wasm_native_to_interp_ftndescs [37].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "GRGlGetProcDelegateProxyImplementation", 2);
  }
  ((WasmInterpEntrySig_37)wasm_native_to_interp_ftndescs [37].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [37].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_38) (int*, int*, int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_GRVkGetProcDelegateProxyImplementation (void * arg0, void * arg1, void * arg2, void * arg3) { 
  void * res;
  if (!(WasmInterpEntrySig_38)wasm_native_to_interp_ftndescs [38].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "GRVkGetProcDelegateProxyImplementation", 4);
  }
  ((WasmInterpEntrySig_38)wasm_native_to_interp_ftndescs [38].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, wasm_native_to_interp_ftndescs [38].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_39) (int*, int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKGlyphPathDelegateProxyImplementation (void * arg0, void * arg1, void * arg2) { 
  if (!(WasmInterpEntrySig_39)wasm_native_to_interp_ftndescs [39].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "DelegateProxies", "SKGlyphPathDelegateProxyImplementation", 3);
  }
  ((WasmInterpEntrySig_39)wasm_native_to_interp_ftndescs [39].func) ((int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [39].arg);
}

typedef void (*WasmInterpEntrySig_40) (int*, int*, int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_ReadInternal (void * arg0, void * arg1, void * arg2, void * arg3) { 
  void * res;
  if (!(WasmInterpEntrySig_40)wasm_native_to_interp_ftndescs [40].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "ReadInternal", 4);
  }
  ((WasmInterpEntrySig_40)wasm_native_to_interp_ftndescs [40].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, wasm_native_to_interp_ftndescs [40].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_41) (int*, int*, int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_PeekInternal (void * arg0, void * arg1, void * arg2, void * arg3) { 
  void * res;
  if (!(WasmInterpEntrySig_41)wasm_native_to_interp_ftndescs [41].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "PeekInternal", 4);
  }
  ((WasmInterpEntrySig_41)wasm_native_to_interp_ftndescs [41].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, wasm_native_to_interp_ftndescs [41].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_42) (int*, int*, int*, int*);
int32_t wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_IsAtEndInternal (void * arg0, void * arg1) { 
  int32_t res;
  if (!(WasmInterpEntrySig_42)wasm_native_to_interp_ftndescs [42].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "IsAtEndInternal", 2);
  }
  ((WasmInterpEntrySig_42)wasm_native_to_interp_ftndescs [42].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [42].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_43) (int*, int*, int*, int*);
int32_t wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_HasPositionInternal (void * arg0, void * arg1) { 
  int32_t res;
  if (!(WasmInterpEntrySig_43)wasm_native_to_interp_ftndescs [43].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "HasPositionInternal", 2);
  }
  ((WasmInterpEntrySig_43)wasm_native_to_interp_ftndescs [43].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [43].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_44) (int*, int*, int*, int*);
int32_t wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_HasLengthInternal (void * arg0, void * arg1) { 
  int32_t res;
  if (!(WasmInterpEntrySig_44)wasm_native_to_interp_ftndescs [44].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "HasLengthInternal", 2);
  }
  ((WasmInterpEntrySig_44)wasm_native_to_interp_ftndescs [44].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [44].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_45) (int*, int*, int*, int*);
int32_t wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_RewindInternal (void * arg0, void * arg1) { 
  int32_t res;
  if (!(WasmInterpEntrySig_45)wasm_native_to_interp_ftndescs [45].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "RewindInternal", 2);
  }
  ((WasmInterpEntrySig_45)wasm_native_to_interp_ftndescs [45].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [45].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_46) (int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_GetPositionInternal (void * arg0, void * arg1) { 
  void * res;
  if (!(WasmInterpEntrySig_46)wasm_native_to_interp_ftndescs [46].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "GetPositionInternal", 2);
  }
  ((WasmInterpEntrySig_46)wasm_native_to_interp_ftndescs [46].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [46].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_47) (int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_SeekInternal (void * arg0, void * arg1, void * arg2) { 
  int32_t res;
  if (!(WasmInterpEntrySig_47)wasm_native_to_interp_ftndescs [47].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "SeekInternal", 3);
  }
  ((WasmInterpEntrySig_47)wasm_native_to_interp_ftndescs [47].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [47].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_48) (int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_MoveInternal (void * arg0, void * arg1, int32_t arg2) { 
  int32_t res;
  if (!(WasmInterpEntrySig_48)wasm_native_to_interp_ftndescs [48].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "MoveInternal", 3);
  }
  ((WasmInterpEntrySig_48)wasm_native_to_interp_ftndescs [48].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [48].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_49) (int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_GetLengthInternal (void * arg0, void * arg1) { 
  void * res;
  if (!(WasmInterpEntrySig_49)wasm_native_to_interp_ftndescs [49].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "GetLengthInternal", 2);
  }
  ((WasmInterpEntrySig_49)wasm_native_to_interp_ftndescs [49].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [49].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_50) (int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_DuplicateInternal (void * arg0, void * arg1) { 
  void * res;
  if (!(WasmInterpEntrySig_50)wasm_native_to_interp_ftndescs [50].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "DuplicateInternal", 2);
  }
  ((WasmInterpEntrySig_50)wasm_native_to_interp_ftndescs [50].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [50].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_51) (int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_ForkInternal (void * arg0, void * arg1) { 
  void * res;
  if (!(WasmInterpEntrySig_51)wasm_native_to_interp_ftndescs [51].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "ForkInternal", 2);
  }
  ((WasmInterpEntrySig_51)wasm_native_to_interp_ftndescs [51].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [51].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_52) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_DestroyInternal (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_52)wasm_native_to_interp_ftndescs [52].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedStream", "DestroyInternal", 2);
  }
  ((WasmInterpEntrySig_52)wasm_native_to_interp_ftndescs [52].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [52].arg);
}

typedef void (*WasmInterpEntrySig_53) (int*, int*, int*, int*, int*, int*);
int32_t wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_WriteInternal (void * arg0, void * arg1, void * arg2, void * arg3) { 
  int32_t res;
  if (!(WasmInterpEntrySig_53)wasm_native_to_interp_ftndescs [53].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedWStream", "WriteInternal", 4);
  }
  ((WasmInterpEntrySig_53)wasm_native_to_interp_ftndescs [53].func) ((int*)&res, (int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, wasm_native_to_interp_ftndescs [53].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_54) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_FlushInternal (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_54)wasm_native_to_interp_ftndescs [54].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedWStream", "FlushInternal", 2);
  }
  ((WasmInterpEntrySig_54)wasm_native_to_interp_ftndescs [54].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [54].arg);
}

typedef void (*WasmInterpEntrySig_55) (int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_BytesWrittenInternal (void * arg0, void * arg1) { 
  void * res;
  if (!(WasmInterpEntrySig_55)wasm_native_to_interp_ftndescs [55].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedWStream", "BytesWrittenInternal", 2);
  }
  ((WasmInterpEntrySig_55)wasm_native_to_interp_ftndescs [55].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [55].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_56) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_DestroyInternal (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_56)wasm_native_to_interp_ftndescs [56].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKAbstractManagedWStream", "DestroyInternal", 2);
  }
  ((WasmInterpEntrySig_56)wasm_native_to_interp_ftndescs [56].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [56].arg);
}

typedef void (*WasmInterpEntrySig_57) (int*, int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_DrawInternal (void * arg0, void * arg1, void * arg2) { 
  if (!(WasmInterpEntrySig_57)wasm_native_to_interp_ftndescs [57].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKDrawable", "DrawInternal", 3);
  }
  ((WasmInterpEntrySig_57)wasm_native_to_interp_ftndescs [57].func) ((int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [57].arg);
}

typedef void (*WasmInterpEntrySig_58) (int*, int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_GetBoundsInternal (void * arg0, void * arg1, void * arg2) { 
  if (!(WasmInterpEntrySig_58)wasm_native_to_interp_ftndescs [58].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKDrawable", "GetBoundsInternal", 3);
  }
  ((WasmInterpEntrySig_58)wasm_native_to_interp_ftndescs [58].func) ((int*)&arg0, (int*)&arg1, (int*)&arg2, wasm_native_to_interp_ftndescs [58].arg);
}

typedef void (*WasmInterpEntrySig_59) (int*, int*, int*, int*);
void * wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_NewPictureSnapshotInternal (void * arg0, void * arg1) { 
  void * res;
  if (!(WasmInterpEntrySig_59)wasm_native_to_interp_ftndescs [59].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKDrawable", "NewPictureSnapshotInternal", 2);
  }
  ((WasmInterpEntrySig_59)wasm_native_to_interp_ftndescs [59].func) ((int*)&res, (int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [59].arg);
  return res;
}

typedef void (*WasmInterpEntrySig_60) (int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_DestroyInternal (void * arg0, void * arg1) { 
  if (!(WasmInterpEntrySig_60)wasm_native_to_interp_ftndescs [60].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKDrawable", "DestroyInternal", 2);
  }
  ((WasmInterpEntrySig_60)wasm_native_to_interp_ftndescs [60].func) ((int*)&arg0, (int*)&arg1, wasm_native_to_interp_ftndescs [60].arg);
}

typedef void (*WasmInterpEntrySig_61) (int*, int*, int*, int*, int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKTraceMemoryDump_DumpNumericValueInternal (void * arg0, void * arg1, void * arg2, void * arg3, void * arg4, uint64_t arg5) { 
  if (!(WasmInterpEntrySig_61)wasm_native_to_interp_ftndescs [61].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKTraceMemoryDump", "DumpNumericValueInternal", 6);
  }
  ((WasmInterpEntrySig_61)wasm_native_to_interp_ftndescs [61].func) ((int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, (int*)&arg5, wasm_native_to_interp_ftndescs [61].arg);
}

typedef void (*WasmInterpEntrySig_62) (int*, int*, int*, int*, int*, int*);
void wasm_native_to_interp_SkiaSharp_SkiaSharp_SKTraceMemoryDump_DumpStringValueInternal (void * arg0, void * arg1, void * arg2, void * arg3, void * arg4) { 
  if (!(WasmInterpEntrySig_62)wasm_native_to_interp_ftndescs [62].func) {
   mono_wasm_marshal_get_managed_wrapper ("SkiaSharp","SkiaSharp", "SKTraceMemoryDump", "DumpStringValueInternal", 5);
  }
  ((WasmInterpEntrySig_62)wasm_native_to_interp_ftndescs [62].func) ((int*)&arg0, (int*)&arg1, (int*)&arg2, (int*)&arg3, (int*)&arg4, wasm_native_to_interp_ftndescs [62].arg);
}


static void *wasm_native_to_interp_funcs[] = {
    wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_LoadAssemblyAndGetFunctionPointer, wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_LoadAssembly, wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_LoadAssemblyBytes, wasm_native_to_interp_Internal_Runtime_InteropServices_System_Private_CoreLib_ComponentActivator_GetFunctionPointer, wasm_native_to_interp_System_Globalization_System_Private_CoreLib_CalendarData_EnumCalendarInfoCallback, wasm_native_to_interp_System_Threading_System_Private_CoreLib_ThreadPool_BackgroundJobHandler, wasm_native_to_interp_System_Threading_System_Private_CoreLib_TimerQueue_TimerHandler, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ReleaseDelegateProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GetTableDelegateProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ReleaseDelegateProxyImplementationForMulti, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_FontExtentsProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_NominalGlyphProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_NominalGlyphsProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_VariationGlyphProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphAdvanceProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphAdvancesProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphOriginProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphKerningProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphExtentsProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphContourPointProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphNameProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GlyphFromNameProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_CombiningClassProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_GeneralCategoryProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_MirroringProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ScriptProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_ComposeProxyImplementation, wasm_native_to_interp_HarfBuzzSharp_HarfBuzzSharp_DelegateProxies_DecomposeProxyImplementation, wasm_native_to_interp_MicroCom_Runtime_MicroCom_Runtime_MicroComVtblBase_QueryInterface, wasm_native_to_interp_MicroCom_Runtime_MicroCom_Runtime_MicroComVtblBase_AddRef, wasm_native_to_interp_MicroCom_Runtime_MicroCom_Runtime_MicroComVtblBase_Release, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKBitmapReleaseDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKDataReleaseDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKImageRasterReleaseDelegateProxyImplementationForCoTaskMem, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKImageRasterReleaseDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKImageTextureReleaseDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKSurfaceReleaseDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_GRGlGetProcDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_GRVkGetProcDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_DelegateProxies_SKGlyphPathDelegateProxyImplementation, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_ReadInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_PeekInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_IsAtEndInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_HasPositionInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_HasLengthInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_RewindInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_GetPositionInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_SeekInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_MoveInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_GetLengthInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_DuplicateInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_ForkInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedStream_DestroyInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_WriteInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_FlushInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_BytesWrittenInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKAbstractManagedWStream_DestroyInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_DrawInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_GetBoundsInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_NewPictureSnapshotInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKDrawable_DestroyInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKTraceMemoryDump_DumpNumericValueInternal, wasm_native_to_interp_SkiaSharp_SkiaSharp_SKTraceMemoryDump_DumpStringValueInternal
};

// these strings need to match the keys generated in get_native_to_interp
static const char *wasm_native_to_interp_map[] = {
    "System_Private_CoreLib_ComponentActivator_LoadAssemblyAndGetFunctionPointer", "System_Private_CoreLib_ComponentActivator_LoadAssembly", "System_Private_CoreLib_ComponentActivator_LoadAssemblyBytes", "System_Private_CoreLib_ComponentActivator_GetFunctionPointer", "System_Private_CoreLib_CalendarData_EnumCalendarInfoCallback", "System_Private_CoreLib_ThreadPool_BackgroundJobHandler", "System_Private_CoreLib_TimerQueue_TimerHandler", "HarfBuzzSharp_DelegateProxies_ReleaseDelegateProxyImplementation", "HarfBuzzSharp_DelegateProxies_GetTableDelegateProxyImplementation", "HarfBuzzSharp_DelegateProxies_ReleaseDelegateProxyImplementationForMulti", "HarfBuzzSharp_DelegateProxies_FontExtentsProxyImplementation", "HarfBuzzSharp_DelegateProxies_NominalGlyphProxyImplementation", "HarfBuzzSharp_DelegateProxies_NominalGlyphsProxyImplementation", "HarfBuzzSharp_DelegateProxies_VariationGlyphProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphAdvanceProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphAdvancesProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphOriginProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphKerningProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphExtentsProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphContourPointProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphNameProxyImplementation", "HarfBuzzSharp_DelegateProxies_GlyphFromNameProxyImplementation", "HarfBuzzSharp_DelegateProxies_CombiningClassProxyImplementation", "HarfBuzzSharp_DelegateProxies_GeneralCategoryProxyImplementation", "HarfBuzzSharp_DelegateProxies_MirroringProxyImplementation", "HarfBuzzSharp_DelegateProxies_ScriptProxyImplementation", "HarfBuzzSharp_DelegateProxies_ComposeProxyImplementation", "HarfBuzzSharp_DelegateProxies_DecomposeProxyImplementation", "MicroCom_Runtime_MicroComVtblBase_QueryInterface", "MicroCom_Runtime_MicroComVtblBase_AddRef", "MicroCom_Runtime_MicroComVtblBase_Release", "SkiaSharp_DelegateProxies_SKBitmapReleaseDelegateProxyImplementation", "SkiaSharp_DelegateProxies_SKDataReleaseDelegateProxyImplementation", "SkiaSharp_DelegateProxies_SKImageRasterReleaseDelegateProxyImplementationForCoTaskMem", "SkiaSharp_DelegateProxies_SKImageRasterReleaseDelegateProxyImplementation", "SkiaSharp_DelegateProxies_SKImageTextureReleaseDelegateProxyImplementation", "SkiaSharp_DelegateProxies_SKSurfaceReleaseDelegateProxyImplementation", "SkiaSharp_DelegateProxies_GRGlGetProcDelegateProxyImplementation", "SkiaSharp_DelegateProxies_GRVkGetProcDelegateProxyImplementation", "SkiaSharp_DelegateProxies_SKGlyphPathDelegateProxyImplementation", "SkiaSharp_SKAbstractManagedStream_ReadInternal", "SkiaSharp_SKAbstractManagedStream_PeekInternal", "SkiaSharp_SKAbstractManagedStream_IsAtEndInternal", "SkiaSharp_SKAbstractManagedStream_HasPositionInternal", "SkiaSharp_SKAbstractManagedStream_HasLengthInternal", "SkiaSharp_SKAbstractManagedStream_RewindInternal", "SkiaSharp_SKAbstractManagedStream_GetPositionInternal", "SkiaSharp_SKAbstractManagedStream_SeekInternal", "SkiaSharp_SKAbstractManagedStream_MoveInternal", "SkiaSharp_SKAbstractManagedStream_GetLengthInternal", "SkiaSharp_SKAbstractManagedStream_DuplicateInternal", "SkiaSharp_SKAbstractManagedStream_ForkInternal", "SkiaSharp_SKAbstractManagedStream_DestroyInternal", "SkiaSharp_SKAbstractManagedWStream_WriteInternal", "SkiaSharp_SKAbstractManagedWStream_FlushInternal", "SkiaSharp_SKAbstractManagedWStream_BytesWrittenInternal", "SkiaSharp_SKAbstractManagedWStream_DestroyInternal", "SkiaSharp_SKDrawable_DrawInternal", "SkiaSharp_SKDrawable_GetBoundsInternal", "SkiaSharp_SKDrawable_NewPictureSnapshotInternal", "SkiaSharp_SKDrawable_DestroyInternal", "SkiaSharp_SKTraceMemoryDump_DumpNumericValueInternal", "SkiaSharp_SKTraceMemoryDump_DumpStringValueInternal"
};
