import Pkg

packages = ["Plots", "Images", "ImageIO", "Ripserer", "FileIO"]

for package in packages
    println("Installing ", package)
    Pkg.add(package)
end

Pkg.precompile()
println("Julia TDA packages ready")
