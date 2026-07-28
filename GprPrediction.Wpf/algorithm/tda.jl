ENV["GKSwstype"] = get(ENV, "GKSwstype", "100")

using Plots
using Images
using Ripserer
using FileIO

function progress(message)
    println(message)
    flush(stdout)
end

function read_value_from_model_info(model_info_path, key; default_value="")
    if !isfile(model_info_path)
        return default_value
    end

    wanted = lowercase(strip(key)) * ":"
    for line in eachline(model_info_path)
        stripped = strip(line)
        if isempty(stripped) || startswith(stripped, "#")
            continue
        end

        if startswith(lowercase(stripped), wanted)
            return strip(split(stripped, ":", limit=2)[2])
        end
    end

    return default_value
end

function read_threshold_from_model_info(model_info_path; default_threshold=0.35)
    value = read_value_from_model_info(model_info_path, "tda_threshold"; default_value="")
    if isempty(value)
        progress("TDA threshold not found. Use default threshold: $default_threshold")
        return default_threshold
    end

    try
        return parse(Float64, value)
    catch
        progress("Invalid TDA threshold '$value'. Use default threshold: $default_threshold")
        return default_threshold
    end
end

function normalize_path(base_path, path_value)
    if isabspath(path_value)
        return abspath(path_value)
    end
    return abspath(joinpath(base_path, path_value))
end

function resolve_tda_dir(input_info_path, model_info_path)
    default_tda_dir = joinpath(@__DIR__, ".gpr-runtime", "tda")
    configured_tda_dir = read_value_from_model_info(input_info_path, "tda_dir"; default_value="")
    if isempty(configured_tda_dir)
        configured_tda_dir = read_value_from_model_info(model_info_path, "tda_dir"; default_value=default_tda_dir)
    end
    return normalize_path(@__DIR__, configured_tda_dir)
end

function resolve_tda_threshold(input_info_path, model_info_path; default_threshold=0.35)
    input_threshold = read_value_from_model_info(input_info_path, "tda_threshold"; default_value="")
    if !isempty(input_threshold)
        try
            return parse(Float64, input_threshold)
        catch
            progress("Invalid input_info TDA threshold '$input_threshold'. Use model_info/default threshold.")
        end
    end

    return read_threshold_from_model_info(model_info_path; default_threshold=default_threshold)
end

model_info_path = joinpath(@__DIR__, "model_info.txt")
input_info_path = joinpath(@__DIR__, "data", "input_info.txt")
env_tda_dir = get(ENV, "GPR_TDA_DIR", "")
tda_dir = isempty(env_tda_dir) ? resolve_tda_dir(input_info_path, model_info_path) : normalize_path(@__DIR__, env_tda_dir)
input_image_path = joinpath(tda_dir, "data.jpg")
output_image_path = joinpath(tda_dir, "data.png")
threshold = resolve_tda_threshold(input_info_path, model_info_path; default_threshold=0.35)

progress("TDA threshold: $threshold")
progress("TDA input image: $input_image_path")

if !isfile(input_image_path)
    error("TDA input image not found: $input_image_path")
end

mkpath(dirname(output_image_path))

progress("TDA image loading")
img = load(input_image_path)
img_gray = Gray.(img)
img_array = Array(channelview(img_gray))
img_array = reverse(img_array, dims=1)
img_height, img_width = size(img_array)
progress("TDA image size: $img_width x $img_height")

gr()

progress("TDA homology calculation started")
result = ripserer(Cubical(img_array); cutoff=threshold, reps=true, alg=:homology)
progress("TDA homology calculation finished")

if length(result) >= 2 && length(result[2]) > 0
    lifetimes = [feat.death - feat.birth for feat in result[2]]
    maxlife, minlife = maximum(lifetimes), minimum(lifetimes)
    total_features = length(result[2])
    progress("TDA H1 features: $total_features")
    progress("TDA image rendering started")

    plt = heatmap(
        img_array,
        color = :grays,
        axis = nothing,
        legend = false,
        ticks = nothing,
        border = :none,
        framestyle = :none,
        margin = 0Plots.mm,
        xlims = (0, size(img_array, 2)),
        ylims = (0, size(img_array, 1)),
        size = (size(img_array, 2), size(img_array, 1)),
        aspect_ratio = :equal
    )

    for (i, feat) in enumerate(result[2])
        if feat.representative !== nothing
            lifetime = lifetimes[i]
            alpha = clamp(0.3 + (lifetime - minlife) / (maxlife - minlife + 1e-8), 0.3, 1.0)

            if i == 1 || i % 10 == 0 || i == total_features
                progress("TDA drawing features: $i / $total_features")
            end

            xs = Float64[]
            ys = Float64[]

            for elem in feat.representative
                root = elem.simplex.root
                coeff = elem.coefficient
                if coeff == one(typeof(coeff))
                    v1 = collect(root)
                    v2y = copy(v1)
                    v2y[2] += 1
                    push!(xs, v1[2] / 2)
                    push!(ys, v1[1] / 2)
                    push!(xs, v2y[2] / 2)
                    push!(ys, v2y[1] / 2)
                    push!(xs, NaN)
                    push!(ys, NaN)
                end
            end

            if !isempty(xs)
                plot!(
                    plt,
                    xs,
                    ys,
                    seriestype = :path,
                    color = RGB(0, 0.7, 0),
                    alpha = alpha,
                    linewidth = 1.2
                )
            end
        end
    end
else
    progress("No H1 features: $(basename(input_image_path))")
    progress("TDA image rendering started")
    plt = heatmap(
        img_array,
        color = :grays,
        axis = nothing,
        legend = false,
        ticks = nothing,
        border = :none,
        framestyle = :none,
        margin = 0Plots.mm,
        xlims = (0, size(img_array, 2)),
        ylims = (0, size(img_array, 1)),
        size = (size(img_array, 2), size(img_array, 1)),
        aspect_ratio = :equal
    )
end

progress("TDA output image saving: $output_image_path")
savefig(plt, output_image_path)
progress("TDA output image saved")
